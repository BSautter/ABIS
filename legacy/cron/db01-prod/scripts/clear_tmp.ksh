#!/usr/bin/ksh

cd /tmp

# Delete csv files in the /tmp directory that are older than 28 days and that belong to oracle11g.
find . -name "*.csv" -user oracle11g -mtime +28 -exec rm {} \;
