echo

sqlplus -s  /nolog<<endofshutdown;
connect /  as sysdba;
shutdown immediate;
exit;
endofshutdown

echo deleting old db files...
rm /u01/oradata/abc01/*
rm /u02/oradata/abc01/*
rm /u03/oradata/abc01/*
rm /u04/oradata/abc01/*
rm /u04/oradata/abc01/arch/*

echo copying backup set...
cp /u03/backup/abc01/hot/u01/* /u01/oradata/abc01
cp /u03/backup/abc01/hot/u02/* /u02/oradata/abc01
cp /u03/backup/abc01/hot/u03/* /u03/oradata/abc01
cp /u03/backup/abc01/hot/u04/* /u04/oradata/abc01
cp /u03/backup/abc01/hot/u04/arch/* /u04/oradata/abc01/arch

echo gunziping backup set...
gunzip /u01/oradata/abc01/*.gz
gunzip /u02/oradata/abc01/*.gz
gunzip /u03/oradata/abc01/*.gz
gunzip /u04/oradata/abc01/*.gz
gunzip /u04/oradata/abc01/arch/*.gz

sqlplus  -s /nolog<<endofstartup;
connect / as sysdba;
startup mount;
recover database;
alter database open;
exit;
endofstartup

