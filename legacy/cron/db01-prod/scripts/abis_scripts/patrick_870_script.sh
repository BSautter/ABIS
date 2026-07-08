#!/bin/ksh +xv

echo  "Start processing EDI "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.edi_alcan_870;
#execute dbo.p_edi_wise_870;
EOF
echo "End processing EDI"
