#!/bin/ksh

$ORACLE_HOME/bin/sqlplus dbo/__DB_PASSWORD_REDACTED__<<! 
column customer_id format 999999 heading "CUSTOMER ID"
column date_received format a15 heading "DATE|RECEIVED"
column coil_org_num format a15 heading "COIL ORIGINAL|NUMBER"
column net_wt format 999999 heading "NET|WEIGHT"

set pagesize 60
set LINESIZE 100
set colsep ,     -- separate columns with a comma
 spool /export/home/oracle11g/myfile.csv
 select customer_id, date_received, coil_org_num, net_wt
 from coil
 where customer_id in (1153, 1443, 1459) and
 date_received > (sysdate - 3)
 order by date_received, customer_id, coil_org_num;
 spool off
 exit
 !

 if [ `cat ../myfile.csv | wc -l` -lt 1 ]
 then echo Check Status OK > myfile.csv
 fi
