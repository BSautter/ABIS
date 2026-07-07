#!/bin/ksh +xv 
. /export/home/oracle11g/.profile
ORACLE_SID=act11; export ORACLE_SID
echo  "Export DB"
dmp_file=expat`date +%m%d%y_%H`.dmp
gz_file=expat`date +%m%d%y_%H`.dmp.gz
##/u01/app_11g/product/11.2.0/home/bin/exp system/__DB_PASSWORD_REDACTED__ file=/u02/exp/abc11/$dmp_file full=Y >/u02/exp/abc11/expdata.log #2>&1
/u01/app_11g/product/11.2.0/home/bin/expdp system/__DB_PASSWORD_REDACTED__ directory=dump_dir dumpfile=$dmp_file full=Y logfile=/u03/backup/act11/dump_dir/expdata.log 2>&1
echo "Export DB Completed "
echo "Compress $dmp_file"
#/usr/bin/gzip -f9 /u03/backup/act11/dump_dir/$dmp_file
/usr/bin/gzip -f9 /u03/backup/act11/dump_dir/$dmp_file
echo "Compress finished"

#scp -i /export/home/oracle11g/.ssh/db_bkups /u02/exp/abc11/$gz_file 192.168.1.11:/u02/exp/abc11/$gz_file
#cp /u02/exp/abc11/$gz_file /u02_bk/exp/abc11/$gz_file
