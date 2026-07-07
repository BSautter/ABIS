#!/bin/ksh +xv
. /export/home/oracle11g/.profile

echo  "Import DB"
dmp_file=expat`date +%m%d%y_%H`.dmp
gz_file=expat`date +%m%d%y_%H`.dmp.gz
##/u01/app_11g/product/11.2.0/home/bin/imp system/__DB_PASSWORD_REDACTED__ file=/u02/exp/abc11/$dmp_file full=Y >/u02/exp/abc11/expdata.log #2>&1
/u01/app_11g/product/11.2.0/home/bin/impdp system/__DB_PASSWORD_REDACTED__ directory=DBXFER dumpfile=$dmp_file full=Y log=/u02/exp/abc11/expdata2.log 2>&1
echo "Import DB Completed "


