#!/bin/bash
if [ -f "bootstrap.sh" ];
then
  ./bootstrap.sh
fi

build="packages/Yaaf.AdvancedBuilding/content/build.sh"
chmod +x $build
. $build

do_build $@

#if [ ! -d "temp" ]; then
#  mkdir temp
#fi
#tmp_build="temp/build.sh"
#cp "$build" "$tmp_build"
#chmod +x "$tmp_build"
#"$tmp_build" $@
#exit_code=$?
#rm "$tmp_build"
#exit $exit_code
