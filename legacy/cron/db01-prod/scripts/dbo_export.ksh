#!/bin/ksh +xv 
. /export/home/oracle11g/.profile

echo  "Export DB"
dmp_file=dbo_expat`date +%m%d%y_%H`.dmp
gz_file=dbo_expat`date +%m%d%y_%H`.dmp.gz
##/u01/app_11g/product/11.2.0/home/bin/exp system/__DB_PASSWORD_REDACTED__ file=/u02/exp/abc11/$dmp_file full=Y >/u02/exp/abc11/expdata.log #2>&1
#/u01/app_11g/product/11.2.0/home/bin/expdp system/__DB_PASSWORD_REDACTED__ directory=DBXFER dumpfile=$dmp_file full=Y log=/u02/exp/abc11/expdata2.log 2>&1
/u01/app_11g/product/11.2.0/home/bin/expdp dbo/__DB_PASSWORD_REDACTED__ schemas=dbo consistent=y directory=DBXFER dumpfile=$dmp_file logfile=DBXFER:expdbo.log 2>&1
echo "Export DBO Completed "
echo "Compress $dmp_file"
/usr/bin/gzip -f9 /u02/exp/abc11/$dmp_file
echo "Compress finished"

#scp -i /export/home/oracle11g/.ssh/db_bkups /u02/exp/abc11/$gz_file 192.168.1.11:/u02/exp/abc11/$gz_file
cp /u02/exp/abc11/$gz_file /u02_bk/exp/abc11/$gz_file
