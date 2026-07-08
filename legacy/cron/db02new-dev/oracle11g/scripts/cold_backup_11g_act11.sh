echo

ORACLE_SID=act11; export ORACLE_SID

sqlplus -s  /nolog<<endofshutdown;
connect /  as sysdba;
shutdown immediate;
exit;
endofshutdown

rm /u03/backup/act11/cold/u03/*
rm /u03/backup/act11/cold/u04/*
echo copying...
cp /u03/oradata/act11/* /u03/backup/act11/cold/u03/ 
cp /u04/oradata/act11/* /u03/backup/act11/cold/u04/ 

echo gziping...
gzip /u03/backup/act11/cold/u03/*
gzip /u03/backup/act11/cold/u04/*
cp /u03/backup/act11/cold/u03/* /u03_bk/backup/act11/cold/u03/
cp /u03/backup/act11/cold/u04/* /u03_bk/backup/act11/cold/u04/

scp /u03/backup/act11/cold/u03/* 192.168.1.11:/u03/backup/act11/cold/u03/
scp /u03/backup/act11/cold/u04/* 192.168.1.11:/u03/backup/act11/cold/u04/

sqlplus  -s /nolog<<endofstartup;
connect / as sysdba;
startup;
exit;
endofstartup
