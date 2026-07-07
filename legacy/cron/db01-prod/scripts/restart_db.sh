#!/bin/bash
# Set your environment variables (Modify these to match your system)
export ORACLE_HOME=/u01/app_11g/product/11.2.0/home
export ORACLE_SID=abc11
export PATH=$ORACLE_HOME/bin:$PATH

# Execute the restart using SQL*Plus
sqlplus / as sysdba <<EOF
SHUTDOWN IMMEDIATE;
STARTUP;
EXIT;
EOF

