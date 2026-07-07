#!/usr/bin/bash
. /export/home/oracle11g/.profile

echo  "Start processing EDI "
date

# /u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
sqlplus / as sysdba <<EOF
set serveroutput on;

#12/07/2020. Alex Gerlants. Commented out dbo.edi_alcan_870 as a part of the Include Rebanded Coils project
#execute dbo.edi_alcan_870;

execute dbo.p_create_edi_861_for_all;
execute dbo.p_create_edi_861_for_aleris;
execute dbo.edi_aleris_870;
# commented out 11/02/2016 - execute dbo.edi_alcan_870_reband;
#execute dbo.p_edi_wise_870;
EOF
date
echo "End processing EDI"
