#! /usr/local/bin/perl

#################################################################################################################
# Alex Gerlants. 08/19/2019
# -------------------------
#
# This script will send SMTP email to  one email address (argument 2)
# To call this script, supply 4 arguments:
#     1. email address FROM
#     2. email address TO
#     3. email subject line
#     4. email message/body
#################################################################################################################


use DBI;

$ENV{ORACLE_SID} = 'abc11';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;

# $db='dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523';           #Production
$db='dbi:Oracle:host=192.168.1.11;sid=abc11;port=1523';           #Development

$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

$from = $ARGV[0];
# print "from: $from  \n";

$to = $ARGV[1];
# print "to: $to  \n";

$subject = $ARGV[2];
# print "subject: $subject  \n";

$message = $ARGV[3];
# print "message: $message  \n";

open(MAIL, "|/usr/sbin/sendmail -t");
	
#Email Header
print MAIL "To: $to\n";
print MAIL "From: $from\n";
print MAIL "Subject: $subject\n\n";
#Email Body
print MAIL $message;
close(MAIL);

exit()
