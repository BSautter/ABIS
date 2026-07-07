#!/usr/local/bin/perl
use DBI;
use IO::Socket;#:INET;

#print $DBI::VERSION,"\n";
use DBD::Oracle;

#use CGI;
use CGI qw/:standard/;
use File::Basename;

use Net::Ping;

	my $host = '192.168.10.64';
	my $p = Net::Ping->new('icmp');
    print "$host is ";
    print "NOT " unless $p->ping($host);
    print "reachable.\n";
    $p->close();
