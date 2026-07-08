echo

ORACLE_SID=act11; export ORACLE_SID

sqlplus -s  /nolog<<endofshutdown;
connect /  as sysdba;
shutdown immediate;
exit;
endofshutdown

sqlplus  -s /nolog<<endofstartup;
connect / as sysdba;
startup;
exit;
endofstartup

