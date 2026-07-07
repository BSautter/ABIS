#!/bin/sh +xv

echo  "Checking status unmatched coil "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_check_status_unmatched_coil;
EOF
echo "Checking status unmatched coil completed"
echo "Waiting..."
sleep 15
if test -s /export/home/oracle11g/check_status_unmatched_coil.log 
then
   cat /export/home/oracle11g/check_status_unmatched_coil.log | mailx  -s "List of Status Unmatched Coil" -r "vhuang@albl.com" vhuang@albl.com 
fi
rm /export/home/oracle11g/check_status_unmatched_coil.log
