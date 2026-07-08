#!/bin/sh +xv
date=$1
customer_id=$2
echo  "Creating Recovery Report... "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_recovery_report('$date', '$customer_id')
EOF
echo "Recovery Report Created."
