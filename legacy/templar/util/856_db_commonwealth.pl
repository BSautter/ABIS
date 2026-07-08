#! /usr/local/bin/perl

# Version history
# File Name: postpro_2_db.pl
# 12/01/2002	Bing Jiang	Initial Revison
# 10/23/2013           James Ni             New Revision 
# 12/30/2013    James Ni Updated -

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc11';
#$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
#$db='dbi:Oracle:host=localhost;sid=abc11;port=1523'; 
$db='dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523';
#$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");

$ALERIS	=	"964790856";
#$ALERIS    =   "117791081";
$ABCO_DUNS	=	"039630926T";

$edi_file = $ARGV[0];
%isa = ();
%gs = ();
%st = ();

print "edi_file: $edi_file \n";

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

print "Before IF.   isa{Sndr}: $isa{Sndr}  gs{FuncId}: $gs{FuncId}  st{Type}: $st{Type}  isa{TestIndc}: $isa{TestIndc}  \n";

if ($isa{Sndr} eq $ALERIS && $gs{FuncId} eq "SH" && $st{Type} eq "856" && $isa{TestIndc} eq "P" ) {

   print "After IF. \n";

   @data = split ( /$segDelm/, "$message" );
   foreach $line (@data) {
        $lines .= "$line\n";
}


#open (OUT, ">$INCOMING_856_X12/$X12_file") || die "Cann't create file";
#print OUT $lines;
#close(OUT);

print "file_id:  $file_id  isa{Sndr}: $isa{Sndr}  isa{Rcvr}: $isa{Rcvr}  isa{Date}: $isa{Date}  isa{Time}: $isa{Time}  isa{Control}: $isa{Control}  edi_file: $edi_file \n";

$pre_po = "";
$pre_bol = "";
$item_num = 0;
my $shipment_gross;
my $shipment_net;
my $vendor_part;
my $buyer_part;

# Split the lines
@edi = split(/$lineSep/, $message);

foreach $n(@edi) {
    # Split the EDI Lines
    @elements = split(/$segSep/, $n);
    chomp(@elements);
    
    # Determine each segment
    $segment = $elements[0];

#    print "Before Grab the line for bol. segment: $segment  \n";
 
    # Grab the line for bol
    if ($segment =~ /BSN/) {
        	$bol= $elements[2];
#              print "bol: $bol \n";
    }

    if ($elements[0] eq "TD1" && $elements[6] eq "G") {
       $shipment_gross = $elements[7];
    }

    if ($elements[0] eq "TD1" && $elements[6] eq "N") {
       $shipment_net = $elements[7];
    }

    if ($segment =~ /TD5/) {
        	$scac= $elements[3];
    }

    if ($elements[0] eq "N1" && $elements[1] eq "SF") {
       $ship_from = $elements[4];
    }

    if ($elements[0] eq "N1" && $elements[1] eq "ST") {
       $ship_to = $elements[4];
    }

#    if ($segment =~ /TD3/) {
#        	$vehicle_id= $elements[3];
#    }

    if ($segment =~ /SN1/) {
        	$total_wt= $elements[2];
    }

#    if ($segment =~ /PER/) {
#        	$contact_num= $elements[4];
#    }

=pod
    if ($segment =~ /N1/) {
	use Switch;
	switch ($elements[1]) {
		case "ST"		{$ship_to = $elements[2] }
		case "SF"		{$ship_from = $elements[4]; if ($ship_from =~ /31212326/) {if ($elements[2] =~ /KGN/) {$ship_from =$elements[2]}}  }
	}
   }
=cut

   # if ($elements[0] eq "LIN" && $elements[2] eq "PO") {
   #    $po = $elements[3];
   # }

   if ($elements[0] eq "LIN" && $elements[4] eq "VP") {
      $vendor_part = $elements[5];
   }

   if ($elements[0] eq "LIN" && $elements[6] eq "BP") {
      $buyer_part = $elements[7];
   }

   if ($elements[0] eq "SN1") {
      $total_wt = $elements[2];
   }

   # print "Before Grab line for vo and parts.  shipment_gross: $shipment_gross  shipment_net: $shipment_net  scac: $scac  ship_from: $ship_from  ship_to: $ship_to  vehicle_id: $vehicle_id  total_wt: $total_wt   \n";

   # Grab line for vo and parts
    if ($segment =~ /LIN/) {
              $part = $elements[3];
        	$vo= $elements[5];
    }

#grab line for PO
    if ($elements[0] eq "REF" && $elements[1] eq "PO") {
        $po = $elements[2];

        print "shipment_gross: $shipment_gross  shipment_net: $shipment_net  scac: $scac  ship_from: $ship_from  ship_to: $ship_to  vehicle_id: $vehicle_id  total_wt: $total_wt \n";

        if(($po ne $pre_po)||($bol ne $pre_bol)) {
		#write inbound_transaction and inbound_shipment here

		#Get file id from Oracle sequence object
		$sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
		$sth2->execute;
		$file_id = $sth2->fetchrow();

#        print "Before Update inbound_transaction table   \n";

		#Update inbound_transaction table
		$dbh->do(" INSERT INTO inbound_transaction (	EDI_FILE_ID,
						DUNS_FROM,
						DUNS_TO,
						TRANSACTION_DATE,
						TRANSACTION_TIME,
						INTERCHANGE_CONTROL_NUMBER,
						EDI_FILE_NAME)
					 VALUES ( $file_id,
						'$isa{Sndr}',
						'$isa{Rcvr}',
						'$isa{Date}',
						'$isa{Time}',
						'$isa{Control}',
						'$edi_file' )	
					");
		$dbh->do("commit");

#        print "Before Update inbound_shipment table   \n";

		#Update inbound_shipment table
		$dbh->do(" INSERT INTO inbound_shipment (    EDI_FILE_ID,
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
						TOTAL_WEIGHT)
                 VALUES ( $file_id,
                        '$bol',     
                        $shipment_gross,     
                        $shipment_net,     
                        '$st_hls_td1[1]',     
						'$scac',
						'$vehicle_id',
						'$bol',
						'$ship_to',
						'$ship_from',
						'$vendor_part',
						'$vo',
                        '$po',
						'$contact_num',
						$total_wt )	
                                        ");
		$dbh->do("commit");
#		print "part: $part po: $po \n"; 
		$pre_po = $po;
                $pre_bol = $bol;
	}
    }

#  print "Before Grab the line for coil segment: $segment  \n"; 

   # Grab line for coil
    if ($segment =~ /PID/) {
	use Switch;
	switch ($elements[2]) {
		case "55"		{$alloy = $elements[5] }
		case "16"		{$temper = $elements[5]} 
	}
   }

    if ($segment =~ /MEA/) {
	use Switch;
	switch ($elements[2]) {
		case "WT"		{$netwt = $elements[3] }
		case "WT"		{$grosswt = $elements[3] }
        case "LN"   {$lfeed = $elements[3] }
        case "TH"   {$gauge = $elements[3] }
		case "WD"	{$width = $elements[3] }
		case "GG"	{$gauge = $elements[3] }
		case "DN"	{$density = $elements[3] }
	}
   }

    if ($segment =~ "LIN" && $elements[2] =~ "HN") {
       $lot = $elements[3];
#       print "Inside LIN  \n";
    }

    if ($segment =~ "LIN" && $elements[4] =~ "SN") {
       $pack_id = $elements[5];
#       print "Inside LIN. pack_id: $pack_id  \n";
    }

#    print "Before if segment = REF  segment: $segment  \n";

    if ($segment =~ "REF" && $elements[1] =~ "LT") {
       $customer_coil_num = $elements[2];
#       print "Inside REF  \n";
    }

    if ($segment =~ /DTM/) {
#     print "Before switch in segment DTM  elements[1]: $elements[1]  \n"; 
	use Switch;
	switch ($elements[1]) {
		case "203"	{$cash_date = $elements[2];
        if (length($cash_date) eq 8) {
           $cash_date = substr($cash_date, 4, 2) . "/" . substr($cash_date, 6, 2) . "/" . substr($cash_date, 0, 4);
        }
        else {
           $cash_date = "ERROR";
        }

        $grosswt = $netwt;

#		print "Before Update inbound_coil table. COIL: $coil_id  PACK: $pack_id NETWT:$netwt Width: $width Lfeed: $lfeed Gauge: $gauge Alloy: $alloy Density: $density Temper: $temper  cash_date: $cash_date  netwt: $netwt  grosswt: $grosswt  \n";
        $item_num++; 
		#Update inbound_coil table
		$dbh->do(" insert into inbound_coil(
                         edi_file_id,
                         bol,item_num, 
                         coil_number, 
                         ps_coil_number, 
                         net_weight, 
                         gross_weight,
                         coil_width, 
                         coil_gauge,
                         lot, 
                         pack_id,
                         density,
                         alloy,
                         temper,
                         consumed_coil_num,
                         cash_date
						)
                VALUES ( $file_id,
                         '$bol',
                         $item_num,
                         '$customer_coil_num',
                         '$ps_coil',
                         $netwt,
                         $grosswt,
                         '$width',
                         '$gauge',
                         '$lot',
                         '$pack_id',	
                         '$density',
                         '$alloy',
                         '$temper',
                         '$coil_consumed_id',
                         '$cash_date'
						 )	
                ");
                $dbh->do("commit");
				
				 } 
	}
   }

}

open(MAIL, "/usr/bin/mailx -s 'Aleris EDI 856 received', 'jqni\@albl\.com' < $INCOMING_COIL_856_X12/$X12_file|");
close(MAIL);

} #End of if it is Aleris 856

exit(0);
