ORACLE_SID=abc01; export ORACLE_SID 

echo "Starting backup tablespaces..."
sqlplus -s /nolog <<eof1; 
connect / as sysdba; 
alter tablespace SYSTEM begin backup;
exit;
eof1

echo "start cp /u02/oradata/abc01/sys01.dbf"
cp /u02/oradata/abc01/sys01.dbf /u03/backup/abc01/hot/u02/sys01.dbf

sqlplus -s /nolog <<eof2; 
connect / as sysdba; 
alter tablespace SYSTEM end backup; 
exit;
eof2

sqlplus -s /nolog <<eof3;
connect / as sysdba; 
alter tablespace INDEX_1 begin backup;
exit;
eof3

echo "start cp /u03/oradata/abc01/index01.dbf "
cp /u03/oradata/abc01/index01.dbf /u03/backup/abc01/hot/u03/index01.dbf

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

echo "start cp /u03/oradata/abc01/tools01.dbf"
cp /u03/oradata/abc01/tools01.dbf /u03/backup/abc01/hot/u03/tools01.dbf

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

echo "start cp /u03/oradata/abc01/users01.dbf"
cp /u03/oradata/abc01/users01.dbf /u03/backup/abc01/hot/u03/users01.dbf

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

echo "start cp /u02/oradata/abc01/temp01.dbf"
cp /u02/oradata/abc01/temp01.dbf /u03/backup/abc01/hot/u02/temp01.dbf

sqlplus -s /nolog <<eof10;
connect / as sysdba
alter tablespace TEMP end backup;
exit;
eof10

sqlplus -s /nolog <<eof11;
connect / as sysdba
alter tablespace RBS begin backup;
exit;
eof11

echo "start cp rbs data files..."
cp /u04/oradata/abc01/rbs01.dbf /u03/backup/abc01/hot/u04/rbs01.dbf
cp /u04/oradata/abc01/rbs02.dbf /u03/backup/abc01/hot/u04/rbs02.dbf
cp /u04/oradata/abc01/rbs03.dbf /u03/backup/abc01/hot/u04/rbs03.dbf

sqlplus -s /nolog <<eof12;
connect / as sysdba
alter tablespace RBS end backup;
exit;
eof12

sqlplus -s /nolog <<eof13;
connect / as sysdba
alter tablespace SILVERDOME begin backup;
exit;
eof13

echo "start cp silverdome data files..."
cp /u02/oradata/abc01/silverdome01.dbf /u03/backup/abc01/hot/u02/silverdome01.dbf

sqlplus -s /nolog <<eof14;
connect / as sysdba
alter tablespace SILVERDOME end backup;
exit;
eof14

echo "start backup conftrol files"
sqlplus -s /nolog <<eof15;
connect / as sysdba
alter database backup controlfile to '/u03/backup/abc01/hot/control.bak' reuse;
exit;
eof15

echo "Copy tablespaces complete, now copying ctl files..."
cp /u01/oradata/abc01/ctrl1abc01.ctl  /u03/backup/abc01/hot/u01/ctrl1abc01.ctl
cp /u02/oradata/abc01/ctrl2abc01.ctl  /u03/backup/abc01/hot/u02/ctrl2abc01.ctl
cp /u03/oradata/abc01/ctrl3abc01.ctl  /u03/backup/abc01/hot/u03/ctrl3abc01.ctl
echo "Control file copy complete..."

echo "copy redo log files"
cp /u01/oradata/abc01/redo0101.log /u03/backup/abc01/hot/u01/redo0101.log
cp /u01/oradata/abc01/redo0201.log /u03/backup/abc01/hot/u01/redo0201.log
cp /u01/oradata/abc01/redo0301.log /u03/backup/abc01/hot/u01/redo0301.log
cp /u02/oradata/abc01/redo0102.log /u03/backup/abc01/hot/u02/redo0102.log
cp /u02/oradata/abc01/redo0202.log /u03/backup/abc01/hot/u02/redo0202.log
cp /u02/oradata/abc01/redo0302.log /u03/backup/abc01/hot/u02/redo0302.log

sqlplus -s /nolog <<eof16 
connect / as sysdba;
alter system archive log current;
exit;
eof16

echo "backup arch log files"
cp /u04/oradata/abc01/arch/* /u03/backup/abc01/hot/u04/arch


gzip -f /u03/backup/abc01/hot/u01/*
gzip -f /u03/backup/abc01/hot/u02/*
gzip -f /u03/backup/abc01/hot/u03/*
gzip -f /u03/backup/abc01/hot/u04/*
gzip -f /u03/backup/abc01/hot/u04/arch/*.log
