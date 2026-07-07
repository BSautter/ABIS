#! /usr/local/bin/perl

# Version history
# File Name: 856_2_db_alcan.pl
# 2/24/2007     Bing Jiang      Initial Revison

#File Location
$INCOMING_856_X12       = "/templar/alcan-p/incoming_856_x12";

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc01';
$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

exit
