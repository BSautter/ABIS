#!/bin/ksh +xv 

exec >/export/home/oracle11g/rm_arch_exp.log
echo  "Remove archivelog"
find /u04/oradata/abc11/arch -name '1_*.dbf' -mtime +7 -exec rm -f {} \; -print 
find /u03/oradata/act11/arch -name '1_*.dbf' -mtime +7 -exec rm -f {} \; -print 
find /u04_bk/oradata/abc11/arch  -name '1_*.dbf' -mtime +30 -exec rm -f {} \; -print
#
echo "Remove old export dmp files"
find /u02/exp/abc11 -name 'expat*.gz' -mtime +15 -exec rm -f {} \; -print
find /u02/exp/spc01 -name 'expat*.gz' -mtime +15 -exec rm -f {} \; -print
find /u02_bk/exp/abc11 -name 'expat*.gz' -mtime +15 -exec rm -f {} \; -print
