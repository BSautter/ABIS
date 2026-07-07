#! /usr/local/bin/perl

# Version history
# File Name: postpro_2_db.pl
# 12/01/2002	Bing Jiang	Initial Revison

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

#Get file id from Oracle sequence object
$sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
$sth2->execute;
$file_id = $sth2->fetchrow();

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");

$ALCAN_DUNS	=	"0015049350011G";
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

if ($isa{Sndr} == $ALCAN_DUNS && $gs{FuncId} == "SH" && $st{Type} == "856" ) {

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


@data_seg = split ( /$segDelm/, "$message" );


$st_index = 0;
for $i (2 ..  scalar(@data_seg)-3 ) {
#	print "$data_seg[$i]\n";

	if ($data_seg[$i] =~ /^ST\*856/)  {
	push @st, ($st_index ++);
	next;
	};
	
	push (@{$st[$st_index-1]}, $data_seg[$i]);
}


foreach $st_line (@st) {
	
	@shipment = @{$st[$st_line]};

	@st_bsn = split ( /$elemDelm/, $shipment[0] );
	@st_dtm1 = split ( /$elemDelm/, $shipment[1] );
	@st_dtm2 = split ( /$elemDelm/, $shipment[2] );
	@st_hls = split ( /$elemDelm/, $shipment[3] );
	@st_hls_mea1 = split ( /$elemDelm/, $shipment[4] );
	@st_hls_mea2 = split ( /$elemDelm/, $shipment[5] );
#	@st_hls_mea3 = split ( /$elemDelm/, $shipment[6] );
#	@st_hls_mea4 = split ( /$elemDelm/, $shipment[7] );
	@st_hls_td1 = split ( /$elemDelm/, $shipment[6] );
	@st_hls_td5 = split ( /$elemDelm/, $shipment[7] );
	@st_hls_td3 = split ( /$elemDelm/, $shipment[8] );
	@st_hls_ref1 = split ( /$elemDelm/, $shipment[9] );
	@st_hls_ref2 = split ( /$elemDelm/, $shipment[10] );
	@st_hls_n11 = split ( /$elemDelm/, $shipment[11] );
	@st_hls_n12 = split ( /$elemDelm/, $shipment[12] );
	@st_hlo = split ( /$elemDelm/, $shipment[13] );
	@st_hlo_lin = split ( /$elemDelm/, $shipment[14] );
	@st_hlo_sn1 = split ( /$elemDelm/, $shipment[15] );
	@st_hlo_prf= split ( /$elemDelm/, $shipment[16] );
	@st_hlo_per = split ( /$elemDelm/, $shipment[17] );


	$item_index = 0;
	for $i ( 18 .. scalar (@shipment)-3) {
		if ($shipment[$i] =~ /^HL\*/)  {
		        push @hl_item, ($item_index ++);
			@{hl_item[$item_index-1]} = ();
        		next;
        		};

	        push (@{$hl_item[$item_index-1]}, $shipment[$i]);
	}
	
	
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
                                                '$st_hls_ref1[2]',     
                                                $st_hls_mea1[3],     
                                                $st_hls_mea2[3],     
                                                '$st_hls_td1[1]',     
						'$st_hls_td5[3]',
						'$st_hls_td3[1]',
						'$st_hls_ref2[2]',
						'$st_hls_n11[2]',
						'$st_hls_n12[2]',
						'$st_hlo_lin[3]',
						'$st_hlo_lin[5]',
                                                '$st_hlo_prf[1]',
						'$st_hlo_per[4]',
						$st_hlo_sn1[2] )	
                                        ");
$dbh->do("commit");

		for $item_num ( 0 .. scalar (@hl_item) -1) {
			@hl_item_pid1	=	split ( /$elemDelm/, $hl_item[$item_num][0] );
			@hl_item_pid2	=	split ( /$elemDelm/, $hl_item[$item_num][1] );
			@hl_item_mea1	=	split ( /$elemDelm/, $hl_item[$item_num][6] );
			@hl_item_mea2	=	split ( /$elemDelm/, $hl_item[$item_num][7] );
			@hl_item_mea3	=	split ( /$elemDelm/, $hl_item[$item_num][8] );
			@hl_item_mea4	=	split ( /$elemDelm/, $hl_item[$item_num][9] );
			@hl_item_mea5	=	split ( /$elemDelm/, $hl_item[$item_num][10] );
			@hl_item_ref1	=	split ( /$elemDelm/, $hl_item[$item_num][11] );
			@hl_item_ref2	=	split ( /$elemDelm/, $hl_item[$item_num][12] );
			@hl_item_ref3	=	split ( /$elemDelm/, $hl_item[$item_num][13] );
	
			#Seperate coil number and PS coil number from $hl_item_ref3[2]
			@a_ps_coil = ();
		        @a_ps_coil =  split ( / +/, $hl_item_ref3[2]);
			$ps_coil = $ps_coil_null;
			if ( $a_ps_coil[0] !~ $a_ps_coil[1]) { $ps_coil = $a_ps_coil[1] };
			
#Update inbound_coil table
$dbh->do(" INSERT INTO inbound_coil(		EDI_FILE_ID,
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
						ALLOY,
						TEMPER )
                                         VALUES ( $file_id,
                                                '$st_hls_ref1[2]',
						 $item_num + 1,
                                                '$a_ps_coil[0]',
                                                '$ps_coil',
                                                '$hl_item_mea2[3]',
						$hl_item_mea1[3],
                                               	$hl_item_mea3[3],
						$hl_item_mea4[3],
						$hl_item_mea5[3],
						'$hl_item_ref1[2]',
						'$hl_item_ref2[2]',	
						'$hl_item_pid1[5]',	
						'$hl_item_pid2[5]' )	
                                        ");
$dbh->do("commit");

		
		}

	#reset @hl_item
	@hl_item = ();
}


#open(MAIL, "/usr/bin/mailx -s 'Alcan EDI 856 received', 'bjiang\@albl\.com' < $INCOMING_COIL_856_X12/$X12_file|");
close(MAIL);

} #End of if it is Alcan 856

exit(0);
