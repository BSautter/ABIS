#!/usr/bin/bash
. /export/home/oracle11g/.profile

echo  "Start processing 846 for Cleveland-Cliffs CCSC"
date

# /u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
sqlplus / as sysdba <<EOF
set serveroutput on;

#Alex Gerlants. 06/0802026. 2516 Cleveland-Cliff EDI_CCSC
execute dbo.p_846_cleveland_cliff_ccsc();


echo "Test message" | mailx -s "Test Subject" agerlants@albl.com

EOF
date
echo "End processing EDI"
