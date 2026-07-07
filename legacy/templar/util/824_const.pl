#! /usr/local/bin/perl
use Data::Dumper;


# Alex Gerlants. 05/23/2022. 1241_Constellium-BG_870.
# File Name: 824_const.pl


#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
# 'dbi:Oracle:host=localhost;sid=abc11;port=1523';
my $dbh = DBI->connect( 'dbi:Oracle:host=localhost;sid=abc11;port=1523', 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }


my $CONSTELLIUM_DUNS = "043207177";    #Production and Development
# my $ABCO_DUNS	=	"039630926T";      #Production
my $ABCO_DUNS   =   "2NDSFTP";      #Development

my $OUTGOING_edi	= "/templar/templar/incoming/senddata";
# my $OUTGOING_824_X12  = "/templar/alcan/outgoing_824_x12";
my $OUTGOING_824_X12    = "/templar/templar/incoming/senddata/ToVan_Bkup";

# print "Before my edi_file = ARGV[0]  \n";

$edi_file = $ARGV[0];
%isa = ();
%gs = ();
%st = ();

# print "After my edi_file = ARGV[0]. edi_file: $edi_file  \n";

 my $sender_qualifier = $ARGV[0];
# my $sender_duns = $ARGV[1];
# my $receiver_qualifier = $ARGV[2];
# my $receiver_duns = $ARGV[3];
# my $bol = $ARGV[1];
 my $error_string_in = $ARGV[1];

# print "message: $message  \n";
# print "sender_duns: $sender_duns  receiver_duns: $receiver_duns  bol: $bol  error_string_in: $error_string_in  \n";
# print "\n\nThis is 824_const.pl. edi_file: $edi_file  bol: $bol  error_string_in: $error_string_in  \n";

# exit(0); #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

my %isa = ();
my %gs = ();
my %st = ();

open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
while ( $x = <FILE> ) { $message .= $x; }
close(FILE);

# print "After close(FILE). message: $message  \n";

# Set-up delimiters within the EDI data
$elem = substr( $message, 103, 1 );
$elemDelm = quotemeta $elem;		# Element Separator
$sub = substr( $message, 104, 1 );
$subDelm = quotemeta $sub;			# Sub Element Separator
$seg = substr( $message, 105, 1 );
$segDelm = quotemeta $seg;			# Segment Separator

$lineSep = substr($message, 105, 1);
$segSep  = "\\" . substr($message, 103, 1);

my $message_nospaces = $message;
$message_nospaces =~ s/ //g;
my  @data_nospaces = split ( /$elemDelm/, "$message_nospaces" );

# print "message_nospaces: $message_nospaces  \n";
# print "data_nospaces[13]: $data_nospaces[13]  data_nospaces[28]: $data_nospaces[28]  data_nospaces[29]: $data_nospaces[29]  data_nospaces[30]: $data_nospaces[30]  data_nospaces[31]: $data_nospaces[31]  data_nospaces[32]: $data_nospaces[32]  data_nospaces[33]: $data_nospaces[33]  \n";

my $bol = $data_nospaces[28];

# print "bol: $bol  \n";
$bol2 = $bol;

$isa{AuthQual}	   = $data_nospaces[1];		# Authorization Information Qualifier
$isa{AuthInfo}	   = $data_nospaces[2];		# Authorization Information
$isa{SecQual}	   = $data_nospaces[3];		# Security Information Qualifier
$isa{SecInfo}	   = $data_nospaces[4];		# Security Information
$isa{SndrQual}	   = $data_nospaces[5];		# Sender Interchange ID Qualifier
$isa{Sndr}		   = $data_nospaces[6];		# Interchange Sender ID
$isa{RcvrQual}	   = $data_nospaces[7];		# Receiver Interchange ID Qualifier
$isa{Rcvr}		   = $data_nospaces[8];		# Interchange Receiver ID
$isa{Date}		   = $data_nospaces[9];		# Document Generation Date
$isa{Time}		   = $data_nospaces[10];	# Document Generation Time
$isa{Standard}	   = $data_nospaces[11];	# Interchange Standards ID
$isa{Version}	   = $data_nospaces[12];	# Interchange Version ID
$isa{Control}	   = $data_nospaces[13];	# Interchange Control Number
$isa{AckReq}	   = $data_nospaces[14];	# Acknowledge Requested
$isa{TestIndc}	   = $data_nospaces[15];	# Test Indicator
$isa{element}	   = $elem;        			# Element Separator
$isa{subelement}   = $sub;			       	# Subelement Separator
$isa{segment}	   = $seg;			       	# Segment Separator
$gs{FuncId}		   = $data_nospaces[17];	# Functional ID
$gs{Sndr}		   = $data_nospaces[19];	# Application Sender Code
$gs{Rcvr}		   = $data_nospaces[20];	# Application Receiver Code
$gs{Date}		   = $data_nospaces[21];	# Data Interchange Date
$gs{Time}		   = $data_nospaces[22];	# Data Interchange Time
$gs{Control}	   = $data_nospaces[23];	# Data Interchange Control Number
$gs{Agency}	       = $data_nospaces[24];	# Responsible Agency Code
$gs{Version}	   = $data_nospaces[25];	# Version
$st{Type}		   = $data_nospaces[25];	# Transaction Set ID
$st{Control}	   = $data_nospaces[27];	# Transaction Set Control Number

@data_seg = split ( /$segDelm/, "$message" );

if ( $st{Type} eq "856" ) {
    my $bol = $data_nospaces[28];
}
else {

}

my $isa_control = $isa{Control};

my $receiver = sprintf "%-15s", $isa{Sndr};
my $sender = sprintf "%-15s", $isa{Rcvr};
my $sender_duns = $isa{Rcvr};
my $receiver_duns = $isa{Sndr};
my $isa_control = $isa{Control};


# print "isa{Control}: $isa{Control}  isa{Sndr}: $isa{Sndr}  isa{Rcvr}: $isa{Rcvr} st{Control}: $st{Control} st{Type}: $st{Type}  \n";

my $st_index = 0;
for $i (2 ..  scalar(@data_seg)-3 ) {
#	print "$data_seg[$i]\n";

	if ($data_seg[$i] =~ /^ST/)  {
		push @st, ($st_index ++);
		next;
	};

#	push (@{$st[$st_index-1]}, $data_seg[$i]);
}


if ($isa{Sndr} eq $CONSTELLIUM_DUNS ) {
# if ( $gs{FuncId} eq "SH" && $st{Type} eq "856" ) { # Start of 856 if-statement

    # print "st_index: $st_index\n";


    # Create 824 for this 856 transaction

    $sth3 = $dbh->prepare ( "SELECT edi_gs_log_seq.NEXTVAL from dual");
    $sth3->execute;
    $gs_id = $sth3->fetchrow();
    # $edi_file_824 = "$isa{Control}.edi";

 #   print "Before if (receiver_duns eq CONSTELLIUM_DUNS ).  isa{Sndr}: ==>$isa{Sndr}<==  CONSTELLIUM_DUNS: ==>$CONSTELLIUM_DUNS<==  \n";

# if ( $isa{Sndr} eq $CONSTELLIUM_DUNS ) {

#    print "Inside if (receiver_duns eq CONSTELLIUM_DUNS )  \n";

    ($sec,$min,$hour,$mday,$mon,$year,$wday,$yday,$isdst) = localtime;
    $isa_y_m_d = sprintf ("%02d%02d%02d",$year % 100,$mon+1,$mday);
    $isa_h_m = sprintf ("%02d%02d",$hour,$min);
    $gs_y_m_d = sprintf ("%d%02d%02d",$year+1900,$mon+1,$mday);
    $isa_id = sprintf("%09d",$gs_id);

    $hour = sprintf("%02d", $hour);
    $min = sprintf("%02d", $min);
    $sec = sprintf("%02d", $sec);

    $edi_file_824 = "824_const_" . $gs_y_m_d . "_" . $hour . $min . $sec . ".edi";

    $edi_file_824 = "S_824_const_bol_" . $bol . ".edi"; #Production
    # $edi_file_824 = "s_824_const_" . $gs_id . "_" . $bol . ".edi"; #Development

    #---------------------------------------------------------------------------------------------------------
    # Here I am building string that will have OTI and TED lines to add to the file below.
    #---------------------------------------------------------------------------------------------------------
    
    my $start_pos = 0;
    my $pipe_pos = index($error_string_in, "|", $start_pos);
    my $ted_atring = "";
    my $oti_string = "";
    my $error_string_out = "";
    my $add_2counter = 0;
    
#    print "pipe_pos: $pipe_pos  \n";
    
    if ( $pipe_pos <= -1 ) { # $error_string_in does not have "|"
        $error_string_out = "OTI*TR*SI*" . $isa_control . "~\n" . "TED*ZZZ*" . $error_string_in . " in IEA NUMBER: " . $isa_control . "~\n";
        $add_2counter = 2;
    }
    else { #  $error_string_in has "|"
       $pipe_pos++;

       my $start_pos = 0;
       my $temp = "";
       my $length_error_string_in = length($error_string_in);
       my $counter = 0;
    
       while ( $pipe_pos > -1 ) {
         $counter++;
         # print "pipe_pos: $pipe_pos  start_pos: $start_pos  str_length: $str_length  temp: $temp  \n";
    
         if ( $start_pos > 0 ) {
             $str_length = $pipe_pos - $start_pos;
             $temp = substr($error_string_in, $start_pos - 1, $str_length);
         }
         else {
             $str_length = $pipe_pos - $start_pos - 1;
             $temp = substr($error_string_in, $start_pos, $str_length);
         }
    
         $error_string_out = $error_string_out . "OTI*TR*SI*". $isa_control . "***" . $gs{Rcvr} . "\n" . "TED*ZZZ*" . $temp . " in ISA NUMBER: " . $isa_control . "\n";
         $add_2counter = $add_2counter + 2;

         # print "str_length: $str_length  \n";
         # print "pipe_pos: $pipe_pos  start_pos: $start_pos  str_length: $str_length  temp: $temp  \n";
         # $temp = substr($error_string_in, $start_pos, $str_length);
#        print "length_error_string_in: $length_error_string_in  pipe_pos: $pipe_pos  start_pos: $start_pos  str_length: $str_length  temp: $temp  \n";
         $start_pos = $pipe_pos + 1;
         $pipe_pos = index($error_string_in, "|", $start_pos) + 1;
    
         if ( $pipe_pos == 0 ) { # End of error_string_in
             $str_length = $length_error_string_in - $start_pos + 1;
             $temp = substr($error_string_in, $start_pos - 1, $str_length);
             $error_string_out = $error_string_out . "OTI*TR*SI*". $isa_control . "***" . $gs{Rcvr} . "\n" . "TED*ZZZ*" . $temp . " in ISA NUMBER: " . $isa_control . "\n";
    
             $add_2counter = $add_2counter + 2;
    
#             print "Inside if ( pipe_pos == 0 ). length_error_string_in: $length_error_string_in  pipe_pos: $pipe_pos  start_pos: $start_pos  str_length: $str_length  temp: $temp  \n";
             last;
        }
    
         #if ( $counter > 3 ) {
         #    last;
         #}
       }
    }

#    print "add_2counter: $add_2counter  rror_string_out: $error_string_out  \n";
    
    #---------------------------------------------------------------------------------------------------------


    # print "Before Archive outgoing 824. edi_file_824: $edi_file_824 sender_duns_formatted: ==>$sender_duns_formatted<==  receiver_qualifier_formatted: ==>$receiver_qualifier_formatted<==  \n";
#    print "add_2counter: $add_2counter  OUTGOING_824_X12: $OUTGOING_824_X12  edi_file_824: $edi_file_824  \n";

    #Archive outgoing 824
    my $line_counter = 0;

    open (FILE, ">${OUTGOING_824_X12}/$edi_file_824") or die " Can't create file !!";
	select(FILE);

    #Production
	# print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*P*@\n";

    #Development
    print "ISA*00*          *00*          *$isa{RcvrQual}*$sender*$isa{SndrQual}*$receiver*$isa_y_m_d*$isa_h_m*U*00401*$isa_id*0*T*@~\n"
;
	print "GS*AG*$sender_duns*$receiver_duns*$gs_y_m_d*$isa_h_m*$gs_id*X*004010~\n";
	print "ST*824*0001~\n";
    print "BGN*00*" . $bol . "*$gs_y_m_d*$isa_h_m~\n";
    print "N1*SU**01*$sender_duns~\n";
    print "N1*OU**01*$receiver_duns~\n";

    $line_counter = 5 + $add_2counter;

    #$r1 = chop($error_string_out); #Remove last character. It is carriage return
    #$error_string_out = $error_string_out . " in BOL " . $bol2 ;

    print $error_string_out; # OTI/TED loop

	print	"SE*$line_counter*0001~\n";
	print	"GE*1*$gs_id~\n";
	print	"IEA*1*$isa_id~\n";
	select(STDOUT);
    close(FILE);

#    print "After close(FILE)  \n";

    # Copy file to /templar/templar/incoming/senddata
    system ("cp", "${OUTGOING_824_X12}/$edi_file_824", "${OUTGOING_edi}/$edi_file_824");

}  # if (receiver_duns eq CONSTELLIUM_DUNS ) 

exit(0);

#ST*824*0001   ----824 transaction set id*control number
#AK1*SH*42959	----SH for shipping notice, RC for 861, RS for 870, *42959group control number, same as GE seq received.
#AK9*A*1*1*1 -- A accepted R rejected P partial accepted E accepted with Error noted *1*1*1 means 1 transaction set included, 1 set received, 1 set accepted.

exit(0);
