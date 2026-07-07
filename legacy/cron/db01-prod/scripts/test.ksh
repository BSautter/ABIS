#!/usr/bin/ksh
date '+START: %m/%d/%y %H:%M:%S'
set x `uname -a`
SERVER=$3
RECEIVE="/export/home/oracle11g/edi/receive/"
SEND="/templar/templar/incoming/senddata/"
TEST="412992560"
UTIL="/templar/templar/util/"
WORK="/export/home/oracle11g/scripts/"
typeset -i ret_code  # Set return code to an integer.

ret_code=0 # Initialize return code.

if [[ $SERVER = "db01" ]]; then
        TARGET="412992496"
else
        TARGET="412992560"
fi

echo "The server is: $SERVER and the target is $TARGET"


