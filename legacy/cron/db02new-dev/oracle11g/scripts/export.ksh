#!/bin/ksh +xv
. /export/home/oracle11g/.profile

echo  "Export DB"
dmp_file=expat`date +%m%d%y_%H`.dmp
gz_file=expat`date +%m%d%y_%H`.dmp.gz
/u01/app_11g/product/11.2.0/home/bin/expdp dbo/__DB_PASSWORD_REDACTED__ schemas=dbo consistent=y directory=DBXFER dumpfile=$dmp_file log=/u02/exp/abc11/expdata.log
echo "Export DB Completed "
echo "Compress $dmp_file"
/usr/bin/gzip -f9 /u02/exp/abc11/$dmp_file
echo "Compress finished"
