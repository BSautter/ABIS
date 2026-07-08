#!/usr/local/bin/perl
use IO::Socket;
use CGI qw/:standard/;
use File::Basename;

$cgi = new CGI;
$server_add = '192.168.3.66';
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
               }
print $server_add."'s port# ".$port. " is " . $status.".\n";
