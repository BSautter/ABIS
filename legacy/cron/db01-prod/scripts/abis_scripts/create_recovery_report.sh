#!/bin/bash
. /export/home/oracle11g/.profile
customer_id=$1
echo  "Creating Recovery Report... "
#/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_recovery_report_a_c('`date +%m-%d-%Y`', '$customer_id');
EOF
echo "Recovery Report Created."
