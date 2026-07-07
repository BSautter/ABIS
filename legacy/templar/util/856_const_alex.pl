#! /usr/local/bin/perl

# Alex Gerlants. 05/10/2022. 1241_Constellium-BG_870.
# File Name: 856_const.pl


#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;

#$db='dbi:Oracle:host=localhost;sid=abc11;port=1523'; 
$db='dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523';
# $db='dbi:Oracle:host=192.168.1.11;sid=abc11;port=1523';

#$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");

$CONSTELLIUM_DUNS = "043207177";    #Production and Development

$ABCO_DUNS  =   "039630926T";           #Production
#$ABCO_DUNS	=	"2NDSFTP";               #Test

$edi_file = $ARGV[0];
%isa = ();
%gs = ();
%st = ();

open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
while ( $x = <FILE> ) { $message .= $x; }
close(FILE);

# Set-up delimiters within the EDI data
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

$elem_num = ord($elem);
$sub_num = ord($sub);
$seg_num = ord($seg);
$line_num = ord($line);
$seg_num = ord($seg);

# print "elem_num: $elem_num   sub_num: $sub_num   seg_num: $seg_num   lineSep_num: $lineSep_num   segSep_num: $segSep_num  \n";

$elemDelm_num = ord($elemDelm);
$subDelm_num = ord($subDelm);
$segDelm_num = ord($segDelm);
$lineSep_num = ord($lineSep);
$segSep_num = ord($segSep);

#print "elemDelm_num: $elemDelm_num   subDelm_num: $subDelm_num   segDelm_num: $segDelm_num   lineSep_num: $lineSep_num   segSep_num: $segSep_num  \n";

$message_nospaces = $message;
$message_nospaces =~ s/ //g;
@data_nospaces = split ( /$elemDelm/, "$message_nospaces" );

$isa{AuthQual}          = $data_nospaces[1];            # Authorization Information Qualifier
$isa{AuthInfo}          = $data_nospaces[2];            # Authorization Information
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
$gs{FuncId}             = $data_nospaces[17];           # Functional ID
$gs{Sndr}               = $data_nospaces[19];           # Application Sender Code
$gs{Rcvr}               = $data_nospaces[20];           # Application Receiver Code
$gs{Date}               = $data_nospaces[21];           # Data Interchange Date
$gs{Time}               = $data_nospaces[22];           # Data Interchange Time
$gs{Control}            = $data_nospaces[23];           # Data Interchange Control Number
$gs{Agency}             = $data_nospaces[24];           # Responsible Agency Code
$gs{Version}            = $data_nospaces[25];           # Version
$st{Type}               = $data_nospaces[25];           # Transaction Set ID
$st{Control}            = $data_nospaces[27];           # Transaction Set Control Number

# print "isa{Sndr}: $isa{Sndr}  CONSTELLIUM_DUNS: $CONSTELLIUM_DUNS  gs{FuncId}: $gs{FuncId} st{Type}: $st{Type}  isa{TestIndc}: $isa{TestIndc}  \n";

if ($st{Type} ne "856") {
   exit(0);
}

if ($isa{Sndr} eq $CONSTELLIUM_DUNS && $gs{FuncId} eq "SH" && $st{Type} eq "856" && $isa{TestIndc} eq "P" ) { #Production
#if ($isa{Sndr} eq $CONSTELLIUM_DUNS && $gs{FuncId} eq "SH" && $st{Type} eq "856" && $isa{TestIndc} eq "T" ) { #Development`

# print "After IF \n";

@data = split ( /$segDelm/, "$message" );
foreach $line (@data) {
        $lines .= "$line\n";
} 

#open (OUT, ">$INCOMING_856_X12/$X12_file") || die "Can't create file";
#print OUT $lines;
#close(OUT);

# print "inbound_transaction: file_id: $file_id isa{Sndr}: $isa{Sndr} isa{Rcvr}: $isa{Rcvr} isa{Date}: $isa{Date} isa{Time}: $isa{Time} isa{Control}: $isa{Control}  edi_file: $edi_file \n";

#Alex Gerlants. 12/04/2018. Begin
#Get date/time from Oracle
#$sth3 = $dbh->prepare ( "select to_char(current_date, 'mm/dd/yyyy hh:mi:ss') from dual");
$sth3 = $dbh->prepare ( "select f_get_db_date_time() from dual");
$sth3->execute;
$dt = $sth3->fetchrow();
#print "After dt = . dt: $dt  \n";
#Alex Gerlants. 12/04/2018. End

$pre_po = "";
$pre_bol = "";
$item_num = 0;

# print "Before Split the lines.\n lineSep: ==>$lineSep<== \n  message: $message  \n";

# Split the lines
# Line separator is "*". "*" is a quantifier. Thus we have to add escape "\Q" in front of $lineSep
@edi = split(/\Q$lineSep/, $message);

#print "After Split the lines \n";

#---------------------------------------------------------------------------------------------------------

#Loop through lines looking for missing Coil Number, Cash Date, or Constellium Jobset Job Number
#------------------------------------------------------------------------------------------------

#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------

my $coil_count = 0;
my $dtm_936_missing_count = 0;
my $segment_prev = "";
my $elements1_prev = "";
my $coil_number_missing_string_out = "";
my $cash_date_missing_string_out = "";
my $const_job_number_missing_string_out; #Alex Gerlants. 04/04/2022
my $coil_number_exists = "n";
my $cash_date_exists = "n";
my $const_job_number_exists = "n";  #Alex Gerlants. 04/04/2022
my $length_coil_number = 0;
my $length_cash_date = 0;
my $length_const_job_number = 0;  #Alex Gerlants. 04/04/2022
my $missing_coil_number = 0;
#my message2 = "";

#----------------------------------------------------------
foreach $n(@edi) {
   @elements = split(/$segSep/, $n);
   chomp(@elements);
   $segment = $elements[0]; #Determine each segment

#   print "segment: $segment elements[1]: $elements[1]  elements[2]: $elements[2]   elements[3]: $elements[3]  elements[4]: $elements[4]  elements[15]: $elements[15]  \n";

   if ( $segment eq "BSN" ) {
      $bol = $elements[2];
   }

   if ( $segment eq "HL" && $elements[3] eq "I" ) {
      $coil_count++;
      # print "Inside loop. coil_count: $coil_count  \n";

      if ($coil_count > 1 && $first_alloy_char ne "5" && $cash_date_exists eq "n") {
         $missing_cash_date_coil  = $coil_count - 1;
         $message2 = $message2 . "RECORD " . $missing_cash_date_coil . " HEAT TREATMENT DATE MISSING FOR ALLOY " . $alloy;
      }
      else {
         $cash_date_exists = "n"; #Reset variable
      }

      #print "message2: $message2  \n";
   }

   if ( $segment eq "PID" && $elements[2] eq "55" ) {
      $alloy = $elements[4];
      $first_alloy_char = substr($alloy, 0, 1);
      # print "alloy: $alloy   first_alloy_char: $first_alloy_char  \n";
   }

   if ( $segment eq "LIN" && $elements[12] eq "SE" ) {
      $length_coil_number = length($elements[13]);

      # print "Inside LIN segment, before if ( $length_coil_number <= 0 ). coil_number: $elements[13]  length_coil_number: $length_coil_number  \n";

      if ( $length_coil_number <= 0 ) {
         #$coil_number_exists = "y";
         $message2 = $message2 . "  RECORD " . $coil_count . " COIL NUMBER MISSING  ";
      }
   }

   if ( $segment eq "LIN" && $elements[14] eq "JN" ) {
      $length_const_job_number = length($elements[15]);

      # print "Inside LIN segment. Const. Job number: $elements[15]  length_const_job_number: $length_const_job_number  \n";

      if ( $length_const_job_number <= 0 ) {
         #$const_job_number_missing_string_out = $const_job_number_missing_string_out . "RECORD " . $coil_count . " JOB NUMBER MISSING|";
         $message2 = $message2 . "  RECORD " . $coil_count . " JOB NUMBER MISSING  ";
      }
   }

   if ( $segment eq "DTM" && $elements[1] eq "936" ) {
      $length_cash_date = length($elements[2]);

      if ($length_cash_date > 0) {
         $cash_date_exists = "y";
      }
      # print "Inside DTM 936 IF. length_cash_date: $length_cash_date  cash_date_exists: $cash_date_exists  \n";
   }

   if ( $segment eq "CTT" ) { # End of file. This is for the last coil
      if ($first_alloy_char ne "5" && $cash_date_exists eq "n") {
         $missing_cash_date_coil  = $coil_count - 1;
         $message2 = $message2 . "RECORD " . $missing_cash_date_coil . " HEAT TREATMENT DATE MISSING FOR ALLOY " . $alloy;
      }
   }

   # print "message2: $message2  \n";

} #foreach $n(@edi)

# print "After validation loop. coil_count: $coil_count  \n\n\n"; 

# exit(0); #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
#--------------------------------------------------------

=pod

=cut

#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------

=pod
=cut

=pod
=cut

# print "Before if ( length($message2) > 0 ). message2: $message2  \n";

#my $cmd = "/templar/templar/util/997_856_const.pl " . $edi_file;
#system($cmd);

$length_of_message2 = length($message2);
# print "Before if ( length(message2) > 0 ). message2: $message2  length(message2): $length_of_message2  \n";

if ( length($message2) > 0 ) {
   goto email;
}

=pod
=cut
   #==========================================================================================================
 
#   exit(0); #Do not exit. Constellium will contact us with missing data
            #09/21/2022. Troy and I decided that Constellium will resend ASN if data missing. So exit now.
# } #if ( length($message2) ) > 0

# exit(0); #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------
#--------------------------------------------------------------------------------------------------------------------------

# Begin the actual parsing...
foreach $n(@edi) {

# print "Inside foreach loop Before split. segment: $segment  \n";

    @elements = split(/$segSep/, $n);

#    print "Before chmp() \n";

    chomp(@elements);
    
#    print "segment: $segment \n";

    # Determine each segment
    $segment = $elements[0];
 
    # Grab the line for bol
    if ($segment =~ /BSN/) {
        	$bol= $elements[2];
               @a_delivery_bol=();
               @a_delivery_bol = split(/\+/,$bol);
#               $bol = $a_delivery_bol[0];
#              print "bol: $bol \n";
    }

#   print "segment: $segment  elements[1]: $elements[1] elements[2]: $elements[2] elements[3]: $elements[3] elements[4]: $elements[4] elements[5]: $elements[5] elements[6]: $elements[6] elements[7]: $elements[7] elements[8]: $elements[8] elements[9]: $elements[9] elements[10]: $elements[10] elements[11]: $elements[11] elements[12]: $elements[12] elements[13]: $elements[13] elements[14]: $elements[14]  \n";

    if ($segment eq "TD1" && $elements[6] eq "N") {
        	$netwt = $elements[7];
    }

    if ($segment eq "TD1" && $elements[6] eq "G") {
        	$grosswt = $elements[7];
            $total_wt = $grosswt;
            $td1 = $elements[1]; 
            $packaging_code = $elements[1];
    }

    if ($segment =~ /TD5/) {
        	$scac= $elements[3];
    }

    if ($segment =~ /TD3/) {
        	$vehicle_id= $elements[3];
    }

#    if ($segment =~ /SN1/) {
#        	$total_wt= $elements[2];
#    }

    if ($segment =~ /PER/) {
        	$contact_num= $elements[4];
    }

# Added on 07/25/2019. Begin

# print "segment: $segment  \n";

#print "aan: $n  \n";
# print " elements[12]: $elements[12]  \n";

#  print "segment: $segment  segment_prev: $segment_prev  elements[1]: $elements[1]  elements1_prev: $elements1_prev  elements[2]: $elements[2]  elements[3]: $elements[3]  elements[4]: $elements[4]  elements[5]: $elements[5]  elements[6]: $elements[6]  elements[7]: $elements[7]  elements[8]: $elements[8]  elements[9]: $elements[9]  elements[10]: $elements[10]  elements[11]: $elements[11]  elements[12]: $elements[12]  elements[13]: $elements[13]  elements[14]: $elements[14]  elements[15]: $elements[15]  elements[16]: $elements[16]  elements[17]: $elements[17]  elements[18]: $elements[18]  bol: $bol  coil_count: $coil_count segment_prev: $segment_prev  \n";

    if ($segment eq "REF" && $elements[1] eq "CN") {
      	$bol = $elements[2];
#        print "Inside (segment eq 'REF' && elements[1] eq 'CN'). bol: $bol  \n";
    }

    if ($segment =~ /FOB/) {
            $payment_terms = $elements[1];
    }

    if ($segment =~ /N1/) {
	   use Switch;
	   switch ($elements[1]) {
	      case "SF"		{$ship_from = $elements[4]; $mill_duns_num = $elements[4] }  #Constellium DUNS number
		  case "ST"		{$ship_to = $elements[4] }    #ALBL DUNS
          case "MA"     {$ship_tbd1 = $elements[4] }  #End user DUNS
          case "MF"     {$ship_tbd2 = $elements[4]}   #Steel producer (Constellium) DUNS number
          case "OU"     {$toller_name = $elements[2]; #ALBL DUNS number

          #write inbound_transaction and inbound_shipment here
          #Get file id from Oracle sequence object
          $sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
          $sth2->execute;
          $file_id = $sth2->fetchrow();

#     print "Before update inbound transaction table. EDI file_id: $file_id  \n";
          #Update inbound_transaction table
          $dbh->do(" INSERT INTO inbound_transaction 
                    (	
                       EDI_FILE_ID,
       				   DUNS_FROM,
                  	   DUNS_TO,
                   	   TRANSACTION_DATE,
                   	   TRANSACTION_TIME,
                   	   INTERCHANGE_CONTROL_NUMBER,
                   	   EDI_FILE_NAME
                    )
                    VALUES 
                    ( 
                       $file_id,
                       '$isa{Sndr}',
                       '$isa{Rcvr}',
                       '$isa{Date}',
                       '$isa{Time}',
                       '$isa{Control}',
                       '$edi_file' )	
                   ");
          $dbh->do("commit");

          #Update inbound_shipment table
          $dbh->do(" INSERT INTO inbound_shipment 
                    (
                       EDI_FILE_ID,
                       BOL,
                       GROSS,
                       NET,
                       TD1,
                       SCAC,
                       TRAILER_ID,
                       PK,
                       SHIP_TO,
                       SHIP_FROM,
                       PART_NUMBER,
                       VO,
                       PO,
                       CONTACT_NUMBER,
                       TOTAL_WEIGHT,
                       PACKAGING_CODE,
                       MILL_DUNS_NUM
                     )
                   VALUES 
                     ( 
                       $file_id,
                       '$bol',     
                       $grosswt,     
                       $netwt,     
                       '$td1',     
                       '$scac',
                       '$vehicle_id',
                       '$bol',
                       '$ship_to',
                       '$ship_from',
                       '$part',
                       '$vo',
                       '$po',
                       '$contact_num',
                       $total_wt,
                       '$packaging_code',
                       '$mill_duns_num'
                     )	
                  ");

          $dbh->do("commit");
#          print "part: $part po: $po \n"; 
          # $pre_po = $po;
          $pre_bol = $bol;

          } #case "OU"
       } #switch ($elements[1])
    } #if ($segment =~ /N1/)

    if ($segment =~ /PRF/) {
#          print "Inside PRF  \n";
          $po= $elements[1];
#          $vo = $elements[1];
#          print "Inside PRF. po: $po  vo: $vo  \n";
    } #if ($segment =~ /PRF/)

   # Grab line for coil
   if ($segment eq "REF" && $elements[1] eq "BP") {
       $part = $elements[2];
       $material_num = $elements[2];
    }

    if ($segment =~ /LIN/) {
            $part = $elements[3];
            $material_num = $elements[3];
            $coil_number = $elements[13];  #Alex Gerlants. 11/18/2022. Changed from [13] to [5] as per Lisa's email received on Thu 11/17/2022 4:02 PM
                                           #Alex Gerlants. 01/23/2024. Changed from [5] to [13] as per Lisa's email received on Tue 01/23/2024 2:45 PM
            $lot = $elements[7];

            $po_line_num = $elements[11];
            if ( $po_line_num eq '' ) {
               $po_line_num = '0';
            }

            $vo= $elements[15];
            # $po = $elements[15];
#            print "Inside LIN. part: $part  material_num: $material_num  coil_number: $coil_number  lot: $lot  vo: $vo  \n";
    }

    # print "segment: $segment  \n";

    if ($segment =~ /PID/) {

#     print "Inside if (segment =~ /PID/). elements[2]: $elements[2]  \n";

        use Switch;
        switch ($elements[2]) {
	   	  case "55"		{$alloy = $elements[4]}
             $first_alloy_char = substr($alloy, 0, 1);
             $alloy = substr($elements[4], 0, 4); 
#             print "elements[4]: $elements[4]  alloy: $alloy  \n";
		  # case "16"		{$temper = $elements[5]} 
        } 

        $first_character = substr($alloy, 0, 1);
        $alloy = substr($elements[4], 0, 4);
#        print "Inside SWITCH. alloy: $alloy  first_character: $first_character  \n";

        if ( $first_character eq "5" ) {
           $temper = "O";
        }
        elsif ( $first_character eq "6" ) {
           $temper = "T4";
        }
        else {

           $temper = "-";
        }
   }

if (    ($first_alloy_char ne "5" && $segment eq "DTM" && $elements[1] eq "936") or ($first_alloy_char eq "5" && $segment eq "MEA" && $elements[1] eq "PD" && $elements[2] eq "LN")  ) {
        if ( $first_alloy_char ne "5" ) {
           # Convert from yyyymmdd to mmddyyyy
           my $month_part = substr($elements[2], 4, 2);
           my $day_part = substr($elements[2], 6, 2);
           my $year_part = substr($elements[2], 0, 4);
           $cash_date = $month_part . '/' . $day_part . '/' . $year_part;
        }
        else {
           $lfeed = $elements[3];
           $cash_date = "";           
        } 

        #Update inbound_coil table
        #Alex Gerlants. 12/04/2018. Added date_time_received
 #       print "Before Insert into inbound_coil. EDI file_id: $file_id  coil_number: $coil_number  cash_date: $cash_date  dt: $dt  a_ps_coil[0]: $a_ps_coil[0]   \n";

        $item_num++;
        $dbh->do(" INSERT INTO inbound_coil
        (
            EDI_FILE_ID,
            BOL,
            ITEM_NUM,
            COIL_NUMBER,
            PS_COIL_NUMBER,
            NET_WEIGHT,
            GROSS_WEIGHT,
            LINEAL_FEED,
            COIL_WIDTH,
            COIL_GAUGE,
            LOT,
            PACK_ID,
            DENSITY,
            ALLOY,
            TEMPER,
            CONSUMED_COIL_NUM,
            MATERIAL_NUM,
            CASH_DATE,
            PO,
            PO_LINE_NUM,
            PART_NUM,
            date_time_received,
            vo,
            lfeed
        )
        VALUES
        (
            $file_id,
            '$bol',
            $item_num,
            '$coil_number',
            '$ps_coil',
            $netwt,
            $grosswt,
            '$lfeed',
            '$width',
            '$gauge',
            '$lot',
            '$pack_id',
            '$density',
            '$alloy',
            '$temper',
            '$coil_consumed_id',
            '$material_num',
            '$cash_date',
            '$po',
            $po_line_num,
            '$part',
            '$dt',
            '$vo',
            '$lfeed'
        )
        ");
        $dbh->do("commit");
        # reset all values
        $cash_date = "";
        $temper = "";l


    } #if ($segment eq "DTM" && $elements[1] eq "936")

    if ($segment =~ /MEA/) {
	use Switch;
	switch ($elements[2]) {
		case "WT"		{$netwt = $elements[3];
                         $grosswt = $elements[3]
                        }
		case "WD"	{$width = $elements[3] }
        case "TH"   {$gauge = $elements[3] }
		case "LN"	{$lfeed = $elements[3] } 

		# case "TH"	{$gauge = $elements[3] }
		# case "DN"	{$density = $elements[3]  }
	}
   }

}

email:

#open(MAIL, "/usr/bin/mailx -s 'Novelis EDI 856 received', 'jqni\@albl\.com' < $INCOMING_COIL_856_X12/$X12_file|");
#close(MAIL);

my $cmd = "/templar/templar/util/997_856_const.pl " . $edi_file;
system($cmd);

#$from = 'Alex';
$from = 'Reports';
$to = 'agerlants@albl.com';
#$subject = 'Constellium EDI 856 received';
#$message = "Constellium EDI 856 file '" . $edi_file . "' successfully received.";

=pod
=cut

if ( length($message2) > 0 ) {
   # Create 824 file
   $message2 = "BOL " . $bol . "  " . $message2;
   #$param = "'$edi_file' '$bol' '$message2'";
   $param = "'$edi_file' '$message2'";
   # print "param: $param  \n";
   $cmd = "/templar/templar/util/824_const.pl $param";
   $result = system($cmd);

   close($edi_file);

# -----------------------------------------------------------------------------------

        #Send email
        #----------
        #$from = 'Constellium 856 received';
        $from = 'Reports@albl.com';
        $query = "select email_address from auto_report_emails where report_name = '856 data missing'";
        $message = $message2;
        my $addr = "";

        $dth = $dbh->prepare($query);
        $dth->execute();

        while (  $addr = $dth->fetchrow() ) {
#                 print "After  while (  $addr =      addr: $addr   \n";
                 $to = $addr;


                 # $to = 'agerlants@albl.com';

#                 print "Inside While. After  bol: $bol   to = $addr.  to: $to  \n";

                 # $addr = "agerlant@hotmail.com";
                 # print "Before subject = Data missing in BOL.  message2: $message2   \n";

=pod
                 $subject = "Data missing in BOL " . $bol;
          print "Before my param =. param: ==>$param<==   subject: $subject   bol: $bol  \n";
                 my $param = "'$from', '$to', '$subject' '$message2'";
          print "After my param =. param: ==>$param<==   subject: $subject   bol: $bol  \n"; 
                 print "\n";
                 print "param Data Missing email: $param  \n";
                 print "\n";
                 #$param = $param . "\n\r";
                 my $cmd = "/templar/templar/util/send_email.pl $param";
                 #print "cmd hold for cert email: $cmd  \n";
                 my $result = system($cmd);

=cut
                 #---------------------------------------------------------------------
                 #$message = "Data missing in BOL " . $bol;
                 #$message = $message2;
#                 print "ERROR PART. Before open(MAIL). bol: $bol   to: $to  from: $from  subject: $subject  message: $message  \n";

                 $subject = "Data missing in BOL " . $bol;

                 open(MAIL, "|/usr/sbin/sendmail -t");

                 # Email Header
                 print MAIL "To: $to\n";
                 print MAIL "From: $from\n";
                 print MAIL "Subject: $subject\n\n";
                 #print MAIL "Content-type: text/html\n";
                 # Email Body
                 print MAIL $message;

                 close(MAIL);

#                 print "After close(MAIL). to: $to  from: $from  subject: $subject  message: $message  \n";
                 #---------------------------------------------------------------------

        }

    print "Error Email Sent Successfully\n";

#    print "After 856 load with missing data. bol: $bol   to: $to  from: $from  subject: $subject  message: $message  \n";

}
else {
   $subject = 'Constellium EDI 856 received. 997 created.';
   $message = "Constellium EDI 856 file '" . $edi_file . ". BOL: " . $bol . "' received  successfully. ";
   $to = 'agerlants@albl.com';
   $from = 'reports@albl.com';

#   print "Good part. Before  open(MAIL.   to: $to   from: $from  \n";

   open(MAIL, "|/usr/sbin/sendmail -t");

#   print "Good email. Before  Email Header. bol: $bol   to: $to   from: $from   subject: $subject   message: $message  \n";

   # Email Header
   print MAIL "To: $to\n";
   print MAIL "From: $from\n";
   print MAIL "Subject: $subject\n\n";
   #print MAIL "Content-type: text/html\n";
   # Email Body
   print MAIL $message;

   close(MAIL);
   print "Good Email Sent Successfully\n";

#   print "After successful 856 load. bol: $bol   to: $to  from: $from  subject: $subject  message: $message  \n";
}

}

# print "After uccessful 856 load. bol: $bol   to: $to  from: $from  subject: $subject  message: $message  \n";


exit(0);
