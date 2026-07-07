#!/bin/ksh +xv 
. /export/home/oracle11g/.profile

DATE_TIME=`date +%y%m%d_%H%M`
ORACLE_SID=$1
export ORACLE_SID

/u01/app_11g/product/11.2.0/home/bin/sqlplus /nolog <<EOF
connect / as sysdba
spool ck_io_${DATE_TIME}.log
set time on
select SEGMENT_NAME, BYTES/1024/1024 from dba_segments where owner = 'DBO' order by BYTES;
spool off
EOF
exit

