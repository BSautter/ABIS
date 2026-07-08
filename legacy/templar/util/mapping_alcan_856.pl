#!/usr/local/bin/perl
#
#file name : mapping_alcan_856.pl
#
# Description:  The following script ...... 
#
# Created By:   Bing Jiang
# Date:         Sep 18, 2002
# Modified:     
 
use DBI;
$ENV{ORACLE_SID} = 'abc01';
$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734'; 
#$ENV{TWO_TASK} = 'T:dedb02:abc01';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

$sql1 = "select count(*) from ab_job";

#-------------------------TEST connectivity-----------------------------------
#Get the total matched records number
$sth1 = $dbh->prepare ( $sql1 );
if (!defined $sth1 ) { die "Cannot prepare statement: $DBI::errstr \n"; }
$sth1->execute;
$rowcount = $sth1->fetchrow();

# Disconnect from the database
#$dbh->disconnect;

print "Total Job number is $rowcount \n";
#-----------------------END of TEST--------------------------------------------





#---------------------Insert data to DB ---------------------------------------
#$sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
#$sth2->execute;
#$file_id = $sth2->fetchrow();
#print "Next File ID: $file_id \n";

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( edi_inbound_file_id_seq.NEXTVAL, '23232.222' )");

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");
#$rows = $dbh->do(" INSERT INTO inbound_coil ( ....) VALUES ($l,$d) " );
#$rows = $dbh->do(" INSERT INTO inbound_shipment ( ....) VALUES ($l,$d) " );
#$dbh->do("commit");

exit(1);

