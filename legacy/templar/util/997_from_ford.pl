#! /usr/local/bin/perl

# Version history
# 06/04/2013    Victor Huang Initial Revison
# File name: 997_db_from_ford.pl

#File Location
$INCOMING_997_X12		= "/templar/alcan/incoming_997_x12";

$FORD_ID     =       "F159B";
$ABCO_ID     =       "R0P7A";

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
#$dbh = DBI->connect( 'dbi:Oracle:host=db01.albl.com;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');
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


$message_nospaces = $message;
$message_nospaces =~ s/ //g;
@data_nospaces = split ( /$elemDelm/, "$message_nospaces" );

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
$isa{TestIndc}          = $data_nospaces[15];           # Test Indicator
$isa{element}           = $elem;                        # Element Separator
$isa{subelement}        = $sub;                         # Subelement Separator
$isa{segment}           = $seg;                         # Segment Separator
$gs{FuncId}             = $data_nospaces[18];           # Functional ID
$gs{Sndr}               = $data_nospaces[19];           # Application Sender Code
$gs{Rcvr}               = $data_nospaces[20];           # Application Receiver Code
$gs{Date}               = $data_nospaces[21];           # Data Interchange Date
$gs{Time}               = $data_nospaces[22];           # Data Interchange Time
$gs{Control}            = $data_nospaces[23];           # Data Interchange Control Number
$gs{Agency}             = $data_nospaces[24];           # Responsible Agency Code
$gs{Version}            = $data_nospaces[25];           # Version
$st{Type}               = $data_nospaces[26];           # Transaction Set ID
$st{Control}            = $data_nospaces[27];           # Transaction Set Control Number

$dt = "$gs{Date}$gs{Time}";
#print "$dt\n";

if ($isa{Sndr} == $FORD_ID && $gs{FuncId} == "FA" ) {

if ($st{Type} = "997" ) {
print ("starting 997...\n");
@data_seg = split ( /$segDelm/, "$message" );
#@data_seg_isa = split ( /$elemDelm/, $data_seg[0] );
#@data_seg_gs = split ( /$elemDelm/, $data_seg[1] );

foreach $line (@data_seg) {
#        print "$line\n";
         @line_997 = split ( /$elemDelm/, $line );

         if ($line_997[0] =~ "AK4") {
            # print "997 rejected. Error: $line_997[4] \n";

            system("echo 'A EDI 997 (FA) from FORD has just been received notifying an ASN for shipments of '  $line_997[4] ' was rejected.' | /usr/bin/mailx -s 'Warning: ASN rejected by FORD', 'jqni\@albl\.com' ");
             # Update outbound_edi_transaction
      # $dbh->do(" update outbound_edi_transaction set FA_RECEIVED_TIME = '$dt' where INTERCHANGE_CONTROL_NUMBER = '$line_997[2]'" );
      # $dbh->do("commit");
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
#open (OUT, ">$INCOMING_997_X12/$X12_file") || die "Cann't create file";
#print OUT $lines;
#close(OUT);

}
}
exit (0);
