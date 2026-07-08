#!/bin/bash
. /export/home/oracle11g/.profile
customer_id=$1
echo  "Creating Inventory Report... " $customer_id
echo "execute dbo.p_inventory_report('`date +%m-%d-%Y`', '$customer_id');"
# /u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.p_inventory_report('`date +%m-%d-%Y`', '$customer_id');
-- execute dbo.p_inventory_report('`date +%m-%d-%Y`');
EOF
echo "Inventory Report Created."
