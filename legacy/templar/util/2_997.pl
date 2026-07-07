#! /usr/local/bin/perl
use Data::Dumper;

# Version history
# File Name: 2_997.pl
# 05/02/2005	Bing Jiang	Initial Revison

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
# 'dbi:Oracle:host=localhost;sid=abc11;port=1523';
#my $dbh = DBI->connect( 'dbi:Oracle:host=localhost;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');
my $dbh = DBI->connect( 'dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

my $ALCAN_DUNS	=	"0015049350011G";
my $ABCO_DUNS	=	"039630926T";
my $OUTGOING_997_X12	= "/templar/alcan/outgoing_997_x12";
my $OUTGOING_edi	= "/templar/templar/incoming/senddata";

my $edi_file = $ARGV[0];
my %isa = ();
my %gs = ();
my %st = ();

open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
while ( $x = <FILE> ) { $message .= $x; }
close(FILE);

# Alex Gerlants. 04/02/2019. Arconic EDI
# This should not affect other customers because they don't have "/" on the ISA line
$message =~ s/\\/*/; # Replace "\" with "*" on the ISA line.

# Set-up delimiters within the EDI data
	$elem = substr( $message, 103, 1 );
	$elemDelm = quotemeta $elem;		# Element Separator
	$sub = substr( $message, 104, 1 );
	$subDelm = quotemeta $sub;			# Sub Element Separator
	$seg = substr( $message, 105, 1 );
	$segDelm = quotemeta $seg;			# Segment Separator

my $message_nospaces = $message;
$message_nospaces =~ s/ //g;
my  @data_nospaces = split ( /$elemDelm/, "$message_nospaces" );

$isa{AuthQual}		= $data_nospaces[1];		# Authorization Information Qualifier
$isa{AuthInfo}		= $data_nospaces[2];		# Authorization Information
$isa{SecQual}		= $data_nospaces[3];		# Security Information Qualifier
$isa{SecInfo}		= $data_nospaces[4];		# Security Information
$isa{SndrQual}		= $data_nospaces[5];		# Sender Interchange ID Qualifier
$isa{Sndr}		= $data_nospaces[6];		# Interchange Sender ID
$isa{RcvrQual}		= $data_nospaces[7];		# Receiver Interchange ID Qualifier
$isa{Rcvr}		= $data_nospaces[8];		# Interchange Receiver ID
$isa{Date}		= $data_nospaces[9];		# Document Generation Date
$isa{Time}		= $data_nospaces[10];		# Document Generation Time
$isa{Standard}		= $data_nospaces[11];		# Interchange Standards ID
$isa{Version}		= $data_nospaces[12];		# Interchange Version ID
$isa{Control}		= $data_nospaces[13];		# Interchange Control Number
$isa{AckReq}		= $data_nospaces[14];		# Acknowledge Requested
$isa{TestIndc}		= $data_nospaces[15];		# Test Indicator
$isa{element}		= $elem;			# Element Separator
$isa{subelement}	= $sub;				# Subelement Separator
$isa{segment}		= $seg;				# Segment Separator
$gs{FuncId}		= $data_nospaces[18];		# Functional ID
$gs{Sndr}		= $data_nospaces[19];		# Application Sender Code
$gs{Rcvr}		= $data_nospaces[20];		# Application Receiver Code
$gs{Date}		= $data_nospaces[21];		# Data Interchange Date
$gs{Time}		= $data_nospaces[22];		# Data Interchange Time
$gs{Control}		= $data_nospaces[23];		# Data Interchange Control Number
$gs{Agency}		= $data_nospaces[24];		# Responsible Agency Code
$gs{Version}		= $data_nospaces[25];		# Version
$st{Type}		= $data_nospaces[26];		# Transaction Set ID
$st{Control}		= $data_nospaces[27];		# Transaction Set Control Number

@data_seg = split ( /$segDelm/, "$message" );

my $receiver = sprintf "%-15s", $isa{Sndr};
my $sender = sprintf "%-15s", $isa{Rcvr};

my $st_index = 0;
for $i (2 ..  scalar(@data_seg)-3 ) {
#	print "$data_seg[$i]\n";

	if ($data_seg[$i] =~ /^ST/)  {
		push @st, ($st_index ++);
		next;
	};

#	push (@{$st[$st_index-1]}, $data_seg[$i]);
}

#if ($isa{Sndr} eq $ALCAN_DUNS && $gs{FuncId} eq "SH" && $st{Type} eq "856" ) {
if ( $gs{FuncId} eq "SH" && $st{Type} eq "856" ) { # Start of 856 if-statement

#print "st_index: $st_index\n";
# Create 997 for this 856 transaction

$sth3 = $dbh->prepare ( "SELECT edi_gs_log_seq.NEXTVAL from dual");
$sth3->execute;
$gs_id = $sth3->fetchrow();
$edi_file_997 = "$isa{Control}.edi";

($sec,$min,$hour,$mday,$mon,$year,$wday,$yday,$isdst) = localtime;
#$year4 = $year+1900;
#$year2 = sprintf ("%02d", $year % 100);
$isa_y_m_d = sprintf ("%02d%02d%02d",$year % 100,$mon+1,$mday);
$isa_h_m = sprintf ("%02d%02d",$hour,$min);
$gs_y_m_d = sprintf ("%d%02d%02d",$year+1900,$mon+1,$mday);
$isa_id = sprintf("%09d",$gs_id);

#Archive outgoing 997
open (FILE, ">${OUTGOING_997_X12}/$edi_file_997") or die " Can't create file !!";
	select(FILE);
	print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
	print	"GS*FA*$gs{Rcvr}*$gs{Sndr}*$gs_y_m_d*$isa_h_m*$gs_id*X*004010\n";
	print	"ST*997*0001\n";
	print	"AK1*SH*$gs{Control}\n";
	print	"AK9*A*$st_index*$st_index*$st_index\n";
	print	"SE*4*0001\n";
	print	"GE*1*$gs_id\n";
	print	"IEA*1*$isa_id\n";
	select(STDOUT);
close(FILE);

#
#  Patrick Reynolds, 11/11/2016 - Commented out second output file and inserted system cp statement instead
# to insure that a duplicate 997 was archived.
system ("cp", "${OUTGOING_997_X12}/$edi_file_997", "${OUTGOING_edi}/S_$edi_file_997");
#Put 997 to Templar
# open (FILE, ">${OUTGOING_edi}/S_$edi_file_997") or die " Can't create file !!";
# 	select(FILE);
# 	print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
# 	#print "ISA*00*          *00*          *01*039630926T     *09*0015049350011G *$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
# 	print	"GS*FA*$gs{Rcvr}*$gs{Sndr}*$gs_y_m_d*$isa_h_m*$gs_id*X*004010\n";
# 	print	"ST*997*0001\n";
# 	print	"AK1*SH*$gs{Control}\n";
# 	print	"AK9*A*$st_index*$st_index*$st_index\n";
# 	print	"SE*4*0001\n";
# 	print	"GE*1*$gs_id\n";
# 	print	"IEA*1*$isa_id\n";
# 	select(STDOUT);
# close(FILE);

} # End of 856 if-statement

#if ($isa{Sndr} eq $ALCAN_DUNS && $gs{FuncId} eq "SH" && $st{Type} eq "863" ) { # Start of if-statement
if ($gs{FuncId} eq "SH" && $st{Type} eq "863" ) { # Start of 863 if-statement

# Create 997 for this 863 transaction

$sth3 = $dbh->prepare ( "SELECT edi_gs_log_seq.NEXTVAL from dual");
$sth3->execute;
$gs_id = $sth3->fetchrow();
$edi_file_997 = "$isa{Control}.edi";

($sec,$min,$hour,$mday,$mon,$year,$wday,$yday,$isdst) = localtime;
#$year4 = $year+1900;
#$year2 = sprintf ("%02d", $year % 100);
$isa_y_m_d = sprintf ("%02d%02d%02d",$year % 100,$mon+1,$mday);
$isa_h_m = sprintf ("%02d%02d",$hour,$min);
$gs_y_m_d = sprintf ("%d%02d%02d",$year+1900,$mon+1,$mday);
$isa_id = sprintf("%09d",$gs_id);

#Archive outgoing 997
open (FILE, ">${OUTGOING_997_X12}/$edi_file_997") or die " Can't create file !!";
	select(FILE);
	print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
	#print "ISA*00*          *00*          *01*039630926T     *09*0015049350011G *$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
	print	"GS*FA*$gs{Rcvr}*$gs{Sndr}*$gs_y_m_d*$isa_h_m*$gs_id*X*004010\n";
	print	"ST*997*0001\n";
	print	"AK1*RT*$gs{Control}\n";
	print	"AK9*A*$st_index*$st_index*$st_index\n";
	print	"SE*4*0001\n";
	print	"GE*1*$gs_id\n";
	print	"IEA*1*$isa_id\n";
	select(STDOUT);
close(FILE);

#
#  Patrick Reynolds, 11/11/2016 - Commented out second output file and inserted system cp statement instead
# to insure that a duplicate 997 was archived.
system ("cp", "${OUTGOING_997_X12}/$edi_file_997", "${OUTGOING_edi}/S_$edi_file_997");
#Put 997 to Templar
# open (FILE, ">${OUTGOING_edi}/S_$edi_file_997") or die " Can't create file !!";
# 	select(FILE);
# 	print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
# 	#print "ISA*00*          *00*          *01*039630926T     *09*0015049350011G *$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P**\n";
# 	print	"GS*FA*$gs{Rcvr}*$gs{Sndr}*$gs_y_m_d*$isa_h_m*$gs_id*X*004010\n";
# 	print	"ST*997*0001\n";
# 	print	"AK1*RT*$gs{Control}\n";
# 	print	"AK9*A*$st_index*$st_index*$st_index\n";
# 	print	"SE*4*0001\n";
# 	print	"GE*1*$gs_id\n";
# 	print	"IEA*1*$isa_id\n";
# 	select(STDOUT);
# close(FILE);

} # End of 863 if-statement

#ST*997*0001   ----997 transaction set id*control number
#AK1*SH*42959	----SH for shipping notice, RC for 861, RS for 870, *42959group control number, same as GE seq received.
#AK9*A*1*1*1 -- A accepted R rejected P partial accepted E accepted with Error noted *1*1*1 means 1 transaction set included, 1 set received, 1 set accepted.

exit(0);
