#!/bin/sh +xv
echo  "Checking 997 "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_check_997;
EOF
echo "Checking 997 Completed"
echo "Waiting..."
sleep 5
if test -s /templar/templar/incoming/senddata/check_997.log
then
   cat /templar/templar/incoming/senddata/check_997.log | mailx  -s "List of EDI Files Waiting 997" -r "it-support@albl.com" agerlants@albl.com
fi
rm /templar/templar/incoming/senddata/check_997.log
