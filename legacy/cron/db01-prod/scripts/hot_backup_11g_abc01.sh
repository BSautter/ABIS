ORACLE_SID=abc11; export ORACLE_SID 

echo "Starting backup tablespaces..."

sqlplus -s /nolog <<eof1; 
connect / as sysdba; 
alter tablespace SYSTEM begin backup;
exit;
eof1

echo "start cp /u02/oradata/abc11/system01_abc11.dbf"
cp /u02/oradata/abc11/system01_abc11.dbf /u03/backup/abc11/hot/u02/system01_abc11.dbf

sqlplus -s /nolog <<eof2; 
connect / as sysdba; 
alter tablespace SYSTEM end backup; 
exit;
eof2

sqlplus -s /nolog <<eof1a; 
connect / as sysdba; 
alter tablespace SYSAUX begin backup;
exit;
eof1a

echo "start cp /u02/oradata/abc11/sysaux01_abc11.dbf"
cp /u02/oradata/abc11/sysaux01_abc11.dbf /u03/backup/abc11/hot/u02/sysaux01_abc11.dbf

sqlplus -s /nolog <<eof2a; 
connect / as sysdba; 
alter tablespace SYSAUX end backup; 
exit;
eof2a

sqlplus -s /nolog <<eof3;
connect / as sysdba; 
alter tablespace INDEX_1 begin backup;
exit;
eof3

echo "start cp /u03/oradata/abc11/index01_abc11.dbf "
cp /u03/oradata/abc11/index01_abc11.dbf /u03/backup/abc11/hot/u03/index01_abc11.dbf

sqlplus -s /nolog <<eof4;
connect / as sysdba;
alter tablespace INDEX_1 end backup;
exit;
eof4

sqlplus -s /nolog <<eof5;
connect / as sysdba;
alter tablespace TOOLS begin backup;
exit;
eof5

echo "start cp /u03/oradata/abc11/tools01_abc11.dbf"
cp /u03/oradata/abc11/tools01_abc11.dbf /u03/backup/abc11/hot/u03/tools01_abc11.dbf

sqlplus -s /nolog <<eof6;
connect / as sysdba 
alter tablespace TOOLS end backup;
exit;
eof6

sqlplus -s /nolog <<eof7;
connect / as sysdba
alter tablespace USERS begin backup;
exit;
eof7

echo "start cp /u03/oradata/abc11/users01_abc11.dbf"
cp /u03/oradata/abc11/users01_abc11.dbf /u03/backup/abc11/hot/u03/users01_abc11.dbf

sqlplus -s /nolog <<eof8;
connect / as sysdba
alter tablespace USERS end backup;
exit;
eof8

sqlplus -s /nolog <<eof9;
connect  / as sysdba
alter tablespace TEMP begin backup;
exit;
eof9

echo "start cp /u02/oradata/abc11/temp01_abc11.dbf"
cp /u02/oradata/abc11/temp01_abc11.dbf /u03/backup/abc11/hot/u02/temp01_abc11.dbf

sqlplus -s /nolog <<eof10;
connect / as sysdba
alter tablespace TEMP end backup;
exit;
eof10

sqlplus -s /nolog <<eof11;
connect / as sysdba
alter tablespace UNDOTBS1 begin backup;
exit;
eof11

echo "start cp /u04/oradata/abc11/undotbs01_abc11.dbf"
cp /u04/oradata/abc11/undotbs01_abc11.dbf /u03/backup/abc11/hot/u04/undotbs01_abc11.dbf

sqlplus -s /nolog <<eof12;
connect / as sysdba
alter tablespace UNDOTBS1 end backup;
exit;
eof12

sqlplus -s /nolog <<eof13;
connect / as sysdba
alter tablespace SILVERDOME begin backup;
exit;
eof13

echo "start cp silverdome data files..."
cp /u02/oradata/abc11/silverdome01_abc11.dbf /u03/backup/abc11/hot/u02/silverdome01_abc11.dbf

sqlplus -s /nolog <<eof14;
connect / as sysdba
alter tablespace SILVERDOME end backup;
exit;
eof14

echo "start backup conftrol files"
sqlplus -s /nolog <<eof15;
connect / as sysdba
alter database backup controlfile to '/u03/backup/abc11/hot/control_abc11.bak' reuse;
exit;
eof15

echo "Copy tablespaces complete, now copying ctl files..."
cp /u01/oradata/abc11/ctrl1abc11.ctl  /u03/backup/abc11/hot/u01/ctrl1abc11.ctl
cp /u02/oradata/abc11/ctrl2abc11.ctl  /u03/backup/abc11/hot/u02/ctrl2abc11.ctl
cp /u03/oradata/abc11/ctrl3abc11.ctl  /u03/backup/abc11/hot/u03/ctrl3abc11.ctl
echo "Control file copy complete..."

echo "copy redo log files"
cp /u01/oradata/abc11/redo0101_abc11.log /u03/backup/abc11/hot/u01/redo0101_abc11.log
cp /u01/oradata/abc11/redo0201_abc11.log /u03/backup/abc11/hot/u01/redo0201_abc11.log
cp /u01/oradata/abc11/redo0301_abc11.log /u03/backup/abc11/hot/u01/redo0301_abc11.log
cp /u02/oradata/abc11/redo0102_abc11.log /u03/backup/abc11/hot/u02/redo0102_abc11.log
cp /u02/oradata/abc11/redo0202_abc11.log /u03/backup/abc11/hot/u02/redo0202_abc11.log
cp /u02/oradata/abc11/redo0302_abc11.log /u03/backup/abc11/hot/u02/redo0302_abc11.log

sqlplus -s /nolog <<eof16 
connect / as sysdba;
alter system archive log current;
exit;
eof16

echo "backup arch log files"
cp /u04/oradata/abc11/arch/* /u03/backup/abc11/hot/u04/arch


gzip -f /u03/backup/abc11/hot/u01/*
gzip -f /u03/backup/abc11/hot/u02/*
gzip -f /u03/backup/abc11/hot/u03/*
gzip -f /u03/backup/abc11/hot/u04/*
gzip -f /u03/backup/abc11/hot/u04/arch/*.log
