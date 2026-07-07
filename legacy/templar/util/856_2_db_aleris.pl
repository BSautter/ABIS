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
$db='dbi:Oracle:host=localhost;sid=abc11;port=1523'; 
#$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");

$ALERIS	=	"964790856";
$ABCO_DUNS	=	"039630926T";

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

if ($isa{Sndr} eq $ALERIS && $gs{FuncId} eq "SH" && $st{Type} eq "856" && $isa{TestIndc} eq "P" ) {

@data = split ( /$segDelm/, "$message" );
foreach $line (@data) {
        $lines .= "$line\n";
}


#open (OUT, ">$INCOMING_856_X12/$X12_file") || die "Cann't create file";
#print OUT $lines;
#close(OUT);

#print "inboound_transaction: $file_id $isa{Sndr} $isa{Rcvr} $isa{Date} $isa{Time} $isa{Control} $edi_file \n";

$pre_po = "";
$pre_bol = "";
$item_num = 0;

# Split the lines
@edi = split(/$lineSep/, $message);

foreach $n(@edi) {
    # Split the EDI Lines
    @elements = split(/$segSep/, $n);
    chomp(@elements);
    
    # Determine each segment
    $segment = $elements[0];
 
    # Grab the line for bol
    if ($segment =~ /BSN/) {
        	$bol= $elements[2];
#              print "bol: $bol \n";
    }

    if ($segment =~ /TD5/) {
        	$scac= $elements[3];
    }

    if ($segment =~ /TD3/) {
        	$vehicle_id= $elements[3];
    }

    if ($segment =~ /SN1/) {
        	$total_wt= $elements[2];
    }

    if ($segment =~ /PER/) {
        	$contact_num= $elements[4];
    }

    if ($segment =~ /N1/) {
	use Switch;
	switch ($elements[1]) {
		case "OU"		{$ship_to = $elements[2] }
		case "SF"		{$ship_from = $elements[4]; if ($ship_from =~ /31212326/) {if ($elements[2] =~ 

/KGN/) {$ship_from =$elements[2]}}  }
	}
   }

   # Grab line for vo and parts
    if ($segment =~ /LIN/) {
              $part = $elements[3];
        	$vo= $elements[5];
    }

#grab line for PO
    if ($segment =~ /PRF/) {
              $po = $elements[1];
              if(($po ne $pre_po)||($bol ne $pre_bol)) {
		#write inbound_transaction and inbound_shipment here

		#Get file id from Oracle sequence object
		$sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
		$sth2->execute;
		$file_id = $sth2->fetchrow();

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
                                                $grosswt,     
                                                $netwt,     
                                                '$st_hls_td1[1]',     
						'$scac',
						'$vehicle_id',
						'$bol',
						'$ship_to',
						'$ship_from',
						'$part',
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
		case "N"		{$netwt = $elements[3] }
		case "G"		{$grosswt = $elements[3] }
		case "WD"	{$width = $elements[3] }
		case "LN"	{$lfeed = $elements[3] }
		case "TH"	{$alloy = $elements[3] }
		case "GG"	{$gauge = $elements[3] }
		case "DN"	{$density = $elements[3] }
	}
   }

    if ($segment =~ /REF/) {
	use Switch;
	switch ($elements[1]) {
		case "PK"	{$pack_id = $elements[2] }
		case "LT"	 	{$lot = $elements[2] }
		case "LS"	{$coil_id = $elements[2];
                                 #Seperate coil number and PS coil number
                                 @a_ps_coil=();
                                 @a_ps_coil = split(/ +/,$coil_id);
                                 $ps_coil = $ps_coil_null;
                                 if($a_ps_coil[0] !~ $a_ps_coil[1]){$ps_coil = $a_ps_coil[1]}; 
#				print "COIL: $coil_id  PACK: $pack_id NETWT:$netwt Width: $width Lfeed: $lfeed Gauge: $gauge Alloy: $alloy Density: $density Temper: $temper\n";
                                $item_num++; 
				#Update inbound_coil table
				$dbh->do(" INSERT INTO inbound_coil(EDI_FILE_ID,BOL,ITEM_NUM, COIL_NUMBER, PS_COIL_NUMBER, NET_WEIGHT, GROSS_WEIGHT,LINEAL_FEED, COIL_WIDTH, COIL_GAUGE,LOT, PACK_ID,DENSITY,ALLOY,TEMPER,CONSUMED_COIL_NUM
						)
                                         VALUES ( $file_id,
                                                '$bol',
						 $item_num,
                                                '$a_ps_coil[0]',
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
						'$coil_consumed_id'
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
