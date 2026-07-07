echo  "Start processing EDI "
/u01/app_11g/product/11.2.0/home/bin/sqlplus / as sysdba <<EOF
set serveroutput on;
execute dbo.edi_alcan_870;
#execute dbo.p_create_edi_861_for_all;
#execute dbo.p_edi_wise_870;
#execute dbo.p_edi_novelis_scrap_870;
execute dbo.edi_alcan_scrap_870_by_job;
EOF
echo "End processing EDI"

