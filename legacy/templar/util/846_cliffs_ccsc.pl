#! /usr/local/bin/perl

# Version history
# File Name: postpro_3_db.pl
# 12/01/2002	Bing Jiang	Initial Revision
# 10/23/2013           James Ni             New Revision 
# 12/30/2013    James Ni Updated -

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
$db='dbi:Oracle:host=localhost;sid=abc11;port=1523'; 
#$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

#$edi_file = $ARGV[0];

#open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
#while ( $x = <FILE> ) { $message .= $x; }
#close(FILE);

my $dt;
my $month;
my $day;
my $hour;
my $minute;

#Get date/time from Oracle
#$sth3 = $dbh->prepare ( "select f_get_db_date_time24() from dual");
$sth3 = $dbh->prepare ( "select to_char(current_date, 'mm/dd/yyyy hh24:mi:ss') from dual");
$sth3->execute;
$dt = $sth3->fetchrow();

$month = substr($dt, 0, 2);
$day = substr($dt, 3, 2);
$hour = substr($dt, 11, 2);
$minute =  substr($dt, 14, 2);

#print "edi_file: $edi_file  message: $message  dt: $dt  month: $month  day: $day  hour: $hour  minute: $minute   \n";
print "edi_file: $edi_file   dt: $dt   month: $month   day: $day   hour: $hour   minute: $minute  \n"; 

#exit(0);

#if ($day eq "05" && $hour eq "20") {
#if ($day eq "08" && $hour eq "09" && $minute eq "31") {
#if ( $day eq "08" && ($minute eq "09" || $minute eq "10" || $minute eq "11" || $minute eq "12" || $minute eq "13") ) {
if ( $day eq "08" ) {
   print "Inside IF  \n";

=pod
   $dbh->do
   ("
      declare l_result  number;

      begin
         select   f_846_cleveland_cliff_ccsc(3061)
         into     l_result
         from     dual;
      end;
   ");
=cut

   $dbh->do
   ("
      declare l_result  number;

      begin
         select   f_846_cleveland_cliff_ccsc(3061)
         into     l_result
         from     dual;
      end;
   ");

   $from = 'Alex';
   $to = 'agerlants@albl.com';

   $subject = 'Cleveland-Cliffs 846';
   $message = "Hello from Cleveland-Cliffs 846 in production\nDate/Time: $dt";

   open(MAIL, "|/usr/sbin/sendmail -t");

   # Email Header
   print MAIL "To: $to\n";
   print MAIL "From: $from\n";
   print MAIL "Subject: $subject\n\n";
   #print MAIL "Content-type: text/html\n";
   # Email Body
   print MAIL $message;
}

exit(0);
