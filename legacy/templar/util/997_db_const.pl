#! /usr/local/bin/perl
use Data::Dumper;

# Version history
# 12/12/2007    Bing Jiang      Initial Revison
# File name: 997_db.pl

#File Location
$INCOMING_997_X12		= "/templar/alcan/incoming_997_x12";

$CONSTELLIUM_DUNS = "043207177";   #Production and Development
$ABCO_DUNS        = "039630926T";  #Production

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc01';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
# $dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
# $dbh = DBI->connect( 'dbi:Oracle:abc11', 'dbo/__DB_PASSWORD_REDACTED__');
#$dbh = DBI->connect( 'dbi:Oracle:host=localhost;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');
$dbh = DBI->connect( 'dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');
if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

$edi_file = $ARGV[0];
%isa = ();
%gs = ();
%st = ();

open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
while ( $x = <FILE> ) { $message .= $x; }
close(FILE);

# Set-up delimeters within the EDI data
        $elem = substr( $message, 103, 1 );
          $elemDelm = quotemeta $elem;          # Element Separator
        $sub = substr( $message, 104, 1 );
          $subDelm = quotemeta $sub;            # Sub Element Separator
        $seg = substr( $message, 105, 1 );
          $segDelm = quotemeta $seg;            # Segment Separator

        $lineSep = substr($message, 105, 1);
        $segSep  = "\\" . substr($message, 103, 1);

# print "elem: $elem   sub: $sub   seg: $seg   \n";
# print "elemDelm: $elemDelm   subDelm: $subDelm   segDelm: $segDelm   lineSep: $lineSep   segSep: $segSep  \n";

$message_nospaces = $message;
$message_nospaces =~ s/ //g;
@data_nospaces = split ( /$elemDelm/, "$message_nospaces" );

# print "elem_num: $elem_num   sub_num: $sub_num   seg_num: $seg_num   lineSep_num: $lineSep_num   segSep_num: $segSep_num  \n";

$isa{AuthQual}          = $data_nospaces[1];            # Authorization Information Qualifier
$isa{AuthInfo}          = $data_nospaces[2];            # Authorization Infomation
$isa{SecQual}           = $data_nospaces[3];            # Security Information Qualifier
$isa{SecInfo}           = $data_nospaces[4];            # Security Information
$isa{SndrQual}          = $data_nospaces[5];            # Sender Interchange ID Qualifier
$isa{Sndr}              = $data_nospaces[6];            # Interchange Sender ID
$isa{RcvrQual}          = $data_nospaces[7];            # Receiver Interchange ID Qualifier
$isa{Rcvr}              = $data_nospaces[8];            # Interchange Receiver ID
$isa{Date}              = $data_nospaces[9];            # Document Generation Date
$isa{Time}              = $data_nospaces[10];           # Document Generation Time
$isa{Standard}          = $data_nospaces[11];           # Interchange Standards ID
$isa{Version}           = $data_nospaces[12];           # Interchange Version ID
$isa{Control}           = $data_nospaces[13];           # Interchange Control Number
$isa{AckReq}            = $data_nospaces[14];           # Acknowledge Requested
$isa{element}           = $elem;                        # Element Separator
$isa{subelement}        = $sub;                         # Subelement Separator
$isa{segment}           = $seg;                         # Segment Separator

$gs{FuncId}             = $data_nospaces[17];           # Functional ID
$gs{Sndr}               = $data_nospaces[19];           # Application Sender Code
$gs{Rcvr}               = $data_nospaces[20];           # Application Receiver Code
$gs{Date}               = $data_nospaces[20];           # Data Interchange Date
$gs{Time}               = $data_nospaces[21];           # Data Interchange Time
$gs{Control}            = $data_nospaces[23];           # Data Interchange Control Number
$gs{Agency}             = $data_nospaces[24];           # Responsible Agency Code
$gs{Version}            = $data_nospaces[25];           # Version
$st{Type}               = $data_nospaces[25];           # Transaction Set ID
$st{Control}            = $data_nospaces[27];           # Transaction Set Control Number

$dt = "$gs{Date}$gs{Time}";

=pod
print "Dumper ISA:\n";
print Dumper(%isa);
print "\n\n";

print "Dumper GS:\n";
print Dumper(%gs);
print "\n\n";

print "Dumper ST:\n\n";
print Dumper(%st);
print "\n\n";

print "Dumper DT:\n\n";
print Dumper($dt);
print "\n\n";

print "Dumper isa(ndr), gs(FunctId), st(Type), gs(version)\n\n";
print Dumper($isa{Sndr}), Dumper($gs{FuncId}), Dumper($st{Type}), Dumper($gs{Version});
print "\n\n";

print "Before dt\n\n";
print "dt: $dt  \n";
=cut

print ("isa{Sndr}: $isa{Sndr}    gs{FuncId}: $gs{FuncId}     st{Type}: $st{Type}     edi_file: $edi_file\n\n");

if ($isa{Sndr} eq $CONSTELLIUM_DUNS && $st{Type} eq "997" ) {
	
	if ( $st{Type} eq "997" ) {
		print ("starting 997...\n");
		@data_seg = split ( /$segDelm/, "$message" );
		#@data_seg_isa = split ( /$elemDelm/, $data_seg[0] );
		#@data_seg_gs = split ( /$elemDelm/, $data_seg[1] );
		
		foreach $line (@data_seg) {
#		         print "$line  line[0]: $line[0]  line[1]: $line[1]  line[2]: $line[2]  \n";
		         @line_997 = split ( /$elemDelm/, $line );
		
		         if ($line_997[0] =~ "AK1") {
                     $icn = $line_997[2];
#		             print "result: $line_997[0] $line_997[2] $dt  icn: $icn  \n";
		             # Update outbound_edi_transaction
		       $dbh->do("update outbound_edi_transaction set FA_RECEIVED_TIME = '$dt'
				, FA_RECEIVED_FILE_NAME = '$edi_file' 
				, FA_RECEIVE_STATUS = 1 
				where INTERCHANGE_CONTROL_NUMBER = '$line_997[2]'" );
		       $dbh->do("commit");
		         }
		}
		
		
		#Make a copy to $INCOMING_997_X12
		if ( $edi_file =~ /\// ) {
		        @filename_comp = split( /\//, $edi_file);
		        $X12_file = pop @filename_comp;
		}
		else {
		        $X12_file = $edi_file;
		}
		
		@data = split ( /$segDelm/, "$message" );
		foreach $line (@data) {
		        $lines .= "$line\n";
		}
		
		#print $lines;
		#open (OUT, ">$INCOMING_997_X12/$X12_file") || die "Can't create file";
		#print OUT $lines;
		#close(OUT);
		
	}
}
exit (0);
