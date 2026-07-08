echo

ORACLE_SID=spc01; export ORACLE_SID

sqlplus -s  /nolog<<endofshutdown;
connect /  as sysdba;
shutdown immediate;
exit;
endofshutdown

rm /u03/backup/spc01/cold/u01/*
echo copying...
cp /u01/oradata/spc01/* /u03/backup/spc01/cold/u01/ 

echo gziping...
gzip /u03/backup/spc01/cold/u01/*

sqlplus  -s /nolog<<endofstartup;
connect / as sysdba;
startup;
exit;
endofstartup
