#!/bin/ksh +xv

echo  "checking shift end "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_check_shift_end;
EOF
echo "Checking shift end completed"
