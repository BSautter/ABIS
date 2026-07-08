#!/usr/local/bin/perl
use DBI;
use IO::Socket;
use CGI qw/:standard/;
use File::Basename;
use Net::Ping;

$printerFile = 'printer.txt';
if open($fh, '<:encoding(UTF-8)', $printerFile))
{
print "Content-type: text/html\n\n";
print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
<title>Success</title>
</head>
<body>
<p>
ENDOFTEXT
print 'It worked!';
print <<ENDOFTEXT;
</p>
</body>
</html>
ENDOFTEXT
}
else
{
print "Content-type: text/html\n\n";
print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml"?
<head>
<title>Success</title>
</head>
<body>
<p>
ENDOFTEXT
print 'Didn't work.';
print <<ENDOFTEXT;
</p>
</body>
</html>
ENDOFTEXT
}
