// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
module Yaaf.Shell.Scripting
open Yaaf.Shell
open Yaaf.FSharp.Control
open Yaaf.Shell.StreamModule
open System.Threading.Tasks
open System.Security.Principal
open System.Runtime.InteropServices
open System.Diagnostics

/// http://www.mono-project.com/FAQ:_Technical
let isUnix = 
    let p = int System.Environment.OSVersion.Platform
    (p = 4) || (p = 6) || (p = 128)
/// http://www.mono-project.com/Guide:_Porting_Winforms_Applications
let isMono =
    System.Type.GetType "Mono.Runtime" |> isNull |> not

let escapeArg (arg:string) = 
    sprintf "\"%s\"" (arg.Replace("\"", "\\\""))
module WindowsScripting =
    let isAdmin = 
        let identity = WindowsIdentity.GetCurrent()
        let principal = new WindowsPrincipal(identity)
        principal.IsInRole(WindowsBuiltInRole.Administrator)

    let ensureAdmin () = 
        if isUnix then
            failwith "use only on windows"
        if not isAdmin then
            let exeName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            let startInfo = new ProcessStartInfo(exeName)
            startInfo.Arguments <- 
                (System.Environment.GetCommandLineArgs()
                 |> Seq.skip 1
                 |> Seq.map escapeArg
                 |> String.concat " ")
            if System.Environment.OSVersion.Version.Major >= 6 then  // Windows Vista or higher
                startInfo.Verb <- "runas"
            System.Diagnostics.Process.Start(startInfo) |> ignore
            System.Environment.Exit(0)
            System.Threading.Thread.Sleep 1000

[<DllImport("kernel32.dll", SetLastError = true)>]
extern bool CreateSymbolicLink (string lpSymlinkName, string lpTargetFileName, int dwFlags);

let ln source dest =
    if isUnix then
        let res = Mono.Unix.Native.Syscall.symlink("testfolder", "test")
        if res <> 0 then failwith "symlink failed!"
    else
        let fullSource = System.IO.Path.GetFullPath(source)
        let fullDest = System.IO.Path.GetFullPath(dest)
        printfn "source: %s, dest: %s" fullSource fullDest
        let isDir = if System.IO.Directory.Exists source then 1 else 0
        printfn "isDir: %d" isDir
        let res = CreateSymbolicLink(fullDest, fullSource, isDir)
        if not res then
            let er = Marshal.GetHRForLastWin32Error()
            printfn "err %x" er
            Marshal.ThrowExceptionForHR(er)
            failwith "symlink failed"

type ProgState =
    | Full of IStream<byte[]> * IStream<byte[]> * Task<int>
    | Input of IStream<byte[]>

type Output<'a> = {
    Output : 'a
    Error : 'a
    Task  : Task<int>}
let execFull (ct:System.Threading.CancellationToken) throwOnExitCode prog args prevState =
    let input, exit =
        match prevState with
        | Full (out, _, exit) -> out, Some exit
        | Input inp -> inp, None
    let closeOut, outStream = limitedStream()
    let closeErr, errStream = limitedStream()
    let proc = new ToolProcess(prog, System.Environment.CurrentDirectory, args)
    proc.ThrowOnExitCode <- throwOnExitCode
    async { 
        let! _ = Async.AwaitWaitHandle(ct.WaitHandle)
        try
            proc.Kill()
        with
        | :? System.InvalidOperationException
        | :? System.ComponentModel.Win32Exception -> ()
        
    } |> Async.Start
    let processTask = 
        async {
            let! exitCode = proc.RunAsyncRedirect(input, outStream, errStream)
            do! closeOut()
            do! closeErr()
            match exit with
            | Some e -> 
                do! Async.AwaitTask e |> Async.Ignore // Should throw by itself
                if e.IsFaulted then
                    let capture = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture (e.Exception)
                    capture.Throw()
            | None -> ()
            return exitCode
        } |> Async.StartAsTask // Run them in parallel!
    Full(outStream, errStream, processTask)

let exec prog args prevState = 
    let cts = new System.Threading.CancellationTokenSource()
    execFull cts.Token true prog args prevState

let execTimeout (timeout:System.TimeSpan) prog args prevState = 
    let cts = new System.Threading.CancellationTokenSource()
    cts.CancelAfter(timeout)
    execFull cts.Token true prog args prevState

let read prevState =
    match prevState with
    | Full (out, err, exit) ->
        { Output = out; Error = err; Task = exit }
    | Input _ -> failwith "pipe exec into readdata"

let convertOut convert output =
    { Output = convert output.Output; Error = convert output.Error; Task = output.Task }
    
let ConvStreamReader s = 
    let stream = s |> fromInterface
    new System.IO.StreamReader(stream)
let ConvString (s:System.IO.StreamReader) = s.ReadToEnd()

let rec ConvAsyncSeq (reader:System.IO.StreamReader) = 
    asyncSeq {
        let! line = reader.ReadLineAsync() |> Async.AwaitTask
        if not <| isNull line then
            yield line
            yield! ConvAsyncSeq reader
    }

let makeInput (data:string) = 
    let closeLim, inStream = limitedStream()
    let writer = new System.IO.StreamWriter(inStream |> fromInterface)
    writer.Write data
    closeLim() |> Async.RunSynchronously
    Input inStream
// We can use the normal pipeline operator 
// exec "test" "args" (Input (limitedStream() |> snd)) |> exec "other" "args"


type CopyOptions = 
    | Rec
    | Overwrite 
    | IntegrateExisting 
    | UseExisitingFiles 
    | IgnoreErrors of (System.IO.IOException -> unit)

open System.IO
/// Copes the source directory to the destination with the given CopyOptions
let rec cp (options:SimpleOptions<CopyOptions>) source dest = 
    if options.HasFlag CopyOptions.Overwrite && options.HasFlag CopyOptions.UseExisitingFiles then
        failwith "either overwrite, ignore or none but not both!"
    // assume we have cp /item1 /item2
    let doIgnore = options.HasFlagF CopyOptions.IgnoreErrors
    let ignoreFun e = 
        match options.GetFlagF CopyOptions.IgnoreErrors with
        | CopyOptions.IgnoreErrors f -> f e
        | _ -> printfn "Ignoring: %O" e
    match true with
    |_ when File.Exists source ->
        try
            if File.Exists dest then
                if not <| options.HasFlag CopyOptions.UseExisitingFiles then
                    File.Copy(source, dest, options.HasFlag CopyOptions.Overwrite)
            elif Directory.Exists dest then
                // Copy to /item1/item2
                let name = Path.GetFileName source
                let newDest = Path.Combine(dest, name)
                File.Copy(source, newDest, options.HasFlag CopyOptions.Overwrite)
            else
                File.Copy(source, dest, options.HasFlag CopyOptions.Overwrite)
        with | :? IOException as e when doIgnore ->  ignoreFun e
    |_ when Directory.Exists source ->
        try
            let toCopy = Directory.EnumerateFileSystemEntries(source)
            let doCopy dest items = 
                if options.HasFlag CopyOptions.Rec then
                    items
                    |> Seq.map 
                        (fun item -> 
                            let name = Path.GetFileName item
                            let dest = Path.Combine(dest, name)
                            item, dest)
                    |> Seq.iter
                        (fun (newSrc, newDest) -> cp (options |+ CopyOptions.IntegrateExisting) newSrc newDest)
                else ()
            if Directory.Exists dest then
                // we copy all items into /item2/item1 when IntegrateExisting is not set
                // when IntegrateExisting is set we copy all items into /item2
                let dest =
                    if options.HasFlag CopyOptions.IntegrateExisting then
                        dest
                    else
                        let newDest = Path.Combine(dest, Path.GetFileName source)
                        Directory.CreateDirectory newDest |> ignore
                        newDest
                toCopy
                |> doCopy dest
            else 
                // Just copy to this dir
                Directory.CreateDirectory(dest)|>ignore
                toCopy
                |> doCopy dest
        with | :? IOException as e when doIgnore ->  ignoreFun e
    | _ -> failwith "Source not found!"

let cd dir = 
    // TODO: relative path
    System.Environment.CurrentDirectory <- dir
let getCurrentUser () = 
    Mono.Unix.UnixUserInfo.GetRealUser()
let getEnv () = System.Environment.GetEnvironmentVariables()
let getVar var = 
    System.Environment.GetEnvironmentVariable(var)
let getVarSafe var = 
    let value = getVar var
    if System.String.IsNullOrEmpty value then
        failwithf "Environment Variable %s not found" var
    value
let getEnt () = 
    Mono.Unix.UnixUserInfo.GetLocalUsers()
let getUser (name:string) =
    if System.String.IsNullOrWhiteSpace name then
        failwith "no valid username!"
    Mono.Unix.UnixUserInfo(name)
type ChownOptions =
    | None = 0
    | Rec = 1
let rec doRec traverse f item = 
    match true with
    |_ when File.Exists item ->
        f true item
    |_ when Directory.Exists item ->
        f false item
        if traverse then
            Directory.EnumerateFileSystemEntries(item) 
                |> Seq.iter (fun item -> doRec true f item)
    | _ -> failwithf "item %s not found" item
let chown (options:ChownOptions) (user:Mono.Unix.UnixUserInfo) (group:Mono.Unix.UnixGroupInfo) dest = 
    let chownSimple _ item =
        let entry = Mono.Unix.UnixFileSystemInfo.GetFileSystemEntry item
        entry.SetOwner(user, group)
    
    doRec (options.HasFlag ChownOptions.Rec) chownSimple dest


type CmodOptions =
    | None = 0
    | Rec = 1 

/// http://www.tutorialspoint.com/unix_system_calls/chmod.htm
let chmod (options:CmodOptions) rights dest =
    //let entry = Mono.Unix.UnixFileSystemInfo.GetFileSystemEntry dest
    let chmodSimple _ item =
        let r = Mono.Unix.Native.Syscall.chmod (item, rights)
        Mono.Unix.UnixMarshal.ThrowExceptionForLastErrorIf (r)
    doRec (options.HasFlag CmodOptions.Rec) chmodSimple dest
/// Documentation from http://www.ypass.net/blog/2012/06/introduction-to-syslog-log-levelspriorities/
type Loglevel =
    /// The application has completely crashed and is no longer functioning. Normally, this will generate a message on the console as well as all root terminals. This is the most serious error possible. This should not normally be used applications outside of the system level (filesystems, kernel, etc). This usually means the entire system has crashed.
    | Emergency
    /// The application is unstable and a crash is imminent. This will generate a message on the console and on root terminals. This should not normally be used applications outside of the system level (filesystems, kernel, etc).
    | Alert
    ///  A serious error occurred during application execution. Someone (systems administrators and/or developers) should be notified and should take action to correct the issue.
    | Critical
    /// An error occurred that should be logged, however it is not critical. The error may be transient by nature, but it should be logged to help debug future problems via error message trending. For example, if a connection to a remote server failed, but it will be retried automatically and is fairly self-healing, it is not critical. But if it fails every night at 2AM, you can look through the logs to find the trend.
    | Error
    /// The application encountered a situation that it was not expecting, but it can continue. The application should log the unexpected condition and continue on.
    | Warning
    ///  The application has detected a situation that it was aware of, it can continue, but the condition is possibly incorrect.
    | Notice
    /// For completely informational purposes, the application is simply logging what it is doing. This is useful when trying to find out where an error message is occurring during code execution.
    | Info
    ///  Detailed error messages describing the exact state of internal variables that may be helpful when debugging problems.
    | Debug
type Facility = 
    /// The authorization system: login(1), su(1), getty(8), etc.
    | Auth
    /// The same as SyslogFacility.LOG_AUTH, but logged to a file readable only by selected individuals.
    | AuthPriv
    /// The cron daemon: cron(8).
    | Cron
    /// System daemons, such as routed(8), that are not provided for explicitly by other facilities.
    | Deamon
    /// The file transfer protocol daemons: ftpd(8), tftpd(8), etc.
    | Ftp
    /// Messages generated by the kernel. These cannot be generated by any user processes.
    | Kern
    /// Reserved for local use (0-7).
    | Local of int
    /// The line printer spooling system: lpr(1), lpc(8), lpd(8), etc.
    | Lpr
    /// The mail system.
    | Mail
    /// The network news system.
    | News
    /// Messages generated internally by syslogd(8).
    | Syslog
    /// Messages generated by random user processes. This is the default facility identifier if non is specified.
    | User
    /// The uucp system.
    | Uucp

    
let mutable logger = None
let mutable private isLoggingSet = false
let mutable DefaultFacility = Facility.User
let private convertFacility facility = 
  match facility with
  | Auth -> Mono.Unix.Native.SyslogFacility.LOG_AUTH
  | AuthPriv -> Mono.Unix.Native.SyslogFacility.LOG_AUTHPRIV
  | Cron -> Mono.Unix.Native.SyslogFacility.LOG_CRON
  | Deamon -> Mono.Unix.Native.SyslogFacility.LOG_DAEMON
  | Ftp -> Mono.Unix.Native.SyslogFacility.LOG_FTP
  | Kern -> Mono.Unix.Native.SyslogFacility.LOG_KERN
  | Local i ->
    match i with
    | 0 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL0
    | 1 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL1
    | 2 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL2
    | 3 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL3
    | 4 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL4
    | 5 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL5
    | 6 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL6
    | 7 -> Mono.Unix.Native.SyslogFacility.LOG_LOCAL7
    | _ -> failwith "Invalid local facility number"
                        
  | Lpr -> Mono.Unix.Native.SyslogFacility.LOG_LPR
  | Mail -> Mono.Unix.Native.SyslogFacility.LOG_MAIL
  | News -> Mono.Unix.Native.SyslogFacility.LOG_NEWS
  | Syslog -> Mono.Unix.Native.SyslogFacility.LOG_SYSLOG
  | User -> Mono.Unix.Native.SyslogFacility.LOG_USER
  | Uucp -> Mono.Unix.Native.SyslogFacility.LOG_UUCP
  
let private convertLoglevel loglevel =
  match loglevel with
  | Emergency -> Mono.Unix.Native.SyslogLevel.LOG_EMERG
  | Alert -> Mono.Unix.Native.SyslogLevel.LOG_ALERT
  | Critical -> Mono.Unix.Native.SyslogLevel.LOG_CRIT
  | Debug -> Mono.Unix.Native.SyslogLevel.LOG_DEBUG
  | Error -> Mono.Unix.Native.SyslogLevel.LOG_ERR
  | Info -> Mono.Unix.Native.SyslogLevel.LOG_INFO
  | Notice -> Mono.Unix.Native.SyslogLevel.LOG_NOTICE
  | Warning -> Mono.Unix.Native.SyslogLevel.LOG_WARNING

let private syslogLogger (facility:Facility) (loglevel:Loglevel) (msg:string) =
  let fac = convertFacility facility
  let level = convertLoglevel loglevel
  let sysLog m = Mono.Unix.Native.Syscall.syslog(fac, level, m) |> ignore
  // Syslog seems to hate new lines
  let newLine = System.Environment.NewLine
  if msg.Contains newLine || msg.Contains "\n" then
      let splits = msg.Split ([|newLine; "\n"|], System.StringSplitOptions.None)
      for s in splits do
          sysLog s
  else
      sysLog msg

let setupUnixLogging name options =
    if isLoggingSet then
        failwith "only setup logging once: http://lists.ximian.com/pipermail/mono-list/2010-December/046214.html, man openlog(3)"
    Mono.Unix.Native.Syscall.openlog(
        System.Runtime.InteropServices.Marshal.StringToHGlobalAuto(name),
        options,
        convertFacility DefaultFacility) |> ignore
    isLoggingSet <- true
    logger <- Some syslogLogger

let setupUnixLoggingSafe name options = 
    if not isLoggingSet then
        setupUnixLogging name options

let logFacility fac level (msg:string) = 
    match logger with
    | None -> failwith "setup logging first"
    | Some f -> 
        f fac level msg
let logFacilityf fac level = Printf.ksprintf (logFacility fac level)
let log level = logFacility DefaultFacility level
let logf level = Printf.ksprintf (log level)

open System.Threading                 //Mutex
open System.Security.AccessControl    //MutexAccessRule
open System.Security.Principal        //SecurityIdentifier

// not available on unix!
module GlobalLock = 
    let initMutex name =
        let mutexId = sprintf "Global\\%s" name
        let mutex = new System.Threading.Mutex(false, mutexId)
        
        let allowEveryoneRule = new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), MutexRights.FullControl, AccessControlType.Allow)
        let securitySettings = new MutexSecurity()
        securitySettings.AddAccessRule(allowEveryoneRule)
        mutex.SetAccessControl(securitySettings)
        mutex

    let lockGlobal name (timeout:int) f=
        let mutex = initMutex name
        let mutable hasHandle = false
        try
            try
                hasHandle <- mutex.WaitOne(timeout, false)
                if not hasHandle  then
                    raise <| System.TimeoutException("Timeout waiting for exclusive access")
            with
            | :? AbandonedMutexException -> hasHandle <- true

            f()
        finally
            if hasHandle then
                mutex.ReleaseMutex()