#!/bin/sh +xv
customer_id=0
echo  "Creating Recovery Report... "
sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_recovery_report_a_c('08-03-2015', '$customer_id');
EOF
echo "Recovery Report Created."
