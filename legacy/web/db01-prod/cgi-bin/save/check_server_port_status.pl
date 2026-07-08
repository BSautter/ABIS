#!/usr/local/bin/perl
use IO::Socket;
use CGI qw/:standard/;
use File::Basename;
#use Email::Valid;

$cgi = new CGI;
$server_add = '192.168.1.11';
$port = '3128';
$status = "NULL";

$socket = IO::Socket::INET->new(PeerAddr => $server_add,
                               PeerPort => $port,
                               Proto => 'tcp',
                               Type => SOCK_STREAM);
          if ($socket) {
               $status = "UP";
               close($socket);
               }
         else   {
                 $status = "DOWN";
                 sendEmail("vhuang\@albl.com", "vhuang\@albl.com", "Proxy Server Alert!", "Proxy Server is DOWN");
               }
print $server_add."'s port# ".$port. " is " . $status.".\n";

sub sendEmail
 {
   my ($to, $from, $subject, $message) = @_;
   my $sendmail = '/usr/sbin/sendmail';
   open(MAIL, "|$sendmail -oi -t");
     print MAIL "From: $from\n";
     print MAIL "To: $to\n";
     print MAIL "Subject: $subject\n\n";
     print MAIL "$message\n";
   close(MAIL);
 }
