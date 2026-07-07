echo

ORACLE_SID=act01; export ORACLE_SID

sqlplus -s  /nolog<<endofshutdown;
connect /  as sysdba;
shutdown immediate;
exit;
endofshutdown

rm /u03/backup/act01/cold/u03/*
rm /u03/backup/act01/cold/u04/*
echo copying...
cp /u03/oradata/act01/* /u03/backup/act01/cold/u03/ 
cp /u04/oradata/act01/* /u03/backup/act01/cold/u04/ 

echo gziping...
gzip /u03/backup/act01/cold/u03/*
gzip /u03/backup/act01/cold/u04/*

sqlplus  -s /nolog<<endofstartup;
connect / as sysdba;
startup;
exit;
endofstartup
