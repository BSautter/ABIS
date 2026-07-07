#!/bin/ksh +xv 
. /export/home/oracle11g/.profile

echo  "Datapump DB"
/u01/app_11g/product/11.2.0/home/bin/expdp  system/__DB_PASSWORD_REDACTED__ directory=expdp_dir dumpfile=abc_datapump_full.dmp logfile=abc_datapump_full.log full=Y
echo "Datapump DB Completed "
echo "Compress $dmp_file"
cd /u02_bk/exp/abc11/datapump
mv abc_datapump_full.dmp abc_datapump_full_`date +%m%d%y_%H`.dmp
mv abc_datapump_full.log abc_datapump_full_`date +%m%d%y_%H`.log
/usr/bin/gzip -f9 /u02_bk/exp/abc11/datapump/*.dmp
echo "Compress finished"
#
find /u02_bk/exp/abc11/datapump/ -name 'abc_datapump_full*.gz' -mtime +30 -exec rm -f {} \; -print
find /u02_bk/exp/abc11/datapump/ -name 'abc_datapump_full*.log' -mtime +30 -exec rm -f {} \; -print
#
##scp /u02/exp/abc11/$gz_file 192.168.1.11:/u02/exp/abc11/$gz_file
##cp /u02/exp/abc11/$gz_file /u02_bk/exp/abc11/$gz_file
