#! /usr/local/bin/perl

# Version history
# File Name: 841_2_db.pl
# 03/01/2003	Bing Jiang	Initial Revison

#File Location
$INCOMING_841_X12               = "/templar/alcan/incoming_841_x12";

#Set up DBI environment
use DBI;
$ENV{ORACLE_SID} = 'abc01';
$ENV{ORACLE_HOME} ='/u01/app/oracle/product/734';
eval 'use Oraperl; 1' || die $@ if $] >= 5;
$dbh = DBI->connect( 'dbi:Oracle:abc01', 'dbo/__DB_PASSWORD_REDACTED__');
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

if ($isa{Sndr} == $ALCAN_DUNS && $gs{FuncId} == "SH" && $st{Type} == "841" ) {

#Make a copy to $INCOMING_841_X12
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
open (OUT, ">$INCOMING_841_X12/$X12_file") || die "Cann't create file";
print OUT $lines;
close(OUT);

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

	if ($data_seg[$i] =~ /^ST\*841/)  {
	push @st, ($st_index ++);
	next;
	};
	
	push (@{$st[$st_index-1]}, $data_seg[$i]);
}


foreach $st_line (@st) {
	
	@shipment = @{$st[$st_line]};   # shipment is actually one 841 data set

	@st_spi = split ( /$elemDelm/, $shipment[0] );

	#Get POs which will use 841 data
	for $i ( 1 .. scalar(@shipment) - 3) {
		if ($shipment[$i] =~ /^N1/) {
			last;
		}
		if ($shipment[$i] =~ /^RDT\*Z/) {
			@rdt = split (/$elemDelm/, $shipment[$i]);
			push @po, $rdt[2]; 	
		#	print "RDT: $rdt[2] \n";
			next;
		}
	}

	$i++;

	@st_n1_1 = split ( /$elemDelm/, $shipment[$i ++] );
	@st_n1_2 = split ( /$elemDelm/, $shipment[$i ++] );

	#Get Temper and Alloy between HL*1**24 and HL*2*1*25
	$i = $i + 4;
#print "rdt: $shipment[$i] \n";
	@rdt1 = split ( /$elemDelm/, $shipment[$i ++] );  #revison [4]
#print "pid1: $shipment[$i] \n";
	@pid1 = split ( /$elemDelm/, $shipment[$i ++] );  #alloy [5]
	@pid2 = split ( /$elemDelm/, $shipment[$i ++] );  #temper [5]
	$i = $i + 2;
#print "mea: $shipment[$i] \n";
	@mea = split ( /$elemDelm/, $shipment[$i ++] ); # min[5], max[6]

#print "revison: $rdt1[4]; alloy: $pid1[5]; templar: $pid2[5]; min: $mea[5]; max: $mea[6] \n";
# Insert while to jump to HL*2*1*25
	while ($shipment[$i] !~ /^HL\*2/) {
		$i ++;
	}
	
	$i++;

	if ($shipment[$i] !~ /^CID/ ) {
		print ("CID: $shipment[$i]");  # test if $i is correct
		exit(1);
	}

	#Start  handling CID
		
	$item_index = 0;
	for $j ( $i .. scalar(@shipment) - 3 ){
		if ($shipment[$j] =~ /^CID/)  {
		        push @cid_item, ($item_index ++);
			@{cid_item[$item_index-1]} = ();
        		next;
        		};

	        push (@{$cid_item[$item_index-1]}, $shipment[$j]);
	}

	for $item_num (0 .. scalar (@cid_item) -1) {
		@tmd = split( /$elemDelm/, $cid_item[$item_num][0] );
		if ($tmd[3] =~ /TEI/ ) {
			@tei = split ( /$elemDelm/, $cid_item[$item_num][1] );
			next;
		}
		if ($tmd[3] =~ /TBE/ ) {
                        @tbe = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        next;
                }
		if ($tmd[3] =~ /TWO/ ) {
                        @two = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        next;
                }
		if ($tmd[3] =~ /TPS/ ) {
                        @tps = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        next;
                }
		if ($tmd[3] =~ /TTL/ ) {
                        @ttl = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @ttl_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TTY/ ) {
                        @tty = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tty_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TEL/ ) {
                        @tel = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tel_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TNL/ ) {
                        @tnl = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tnl_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TTS/ ) {
                        @tts = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tts_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TTT/ ) {
                        @ttt = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @ttt_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TET/ ) {
                        @tet = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tet_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TNT/ ) {
                        @tnt = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tnt_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TTO/ ) {
                        @tto = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tto_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TES/ ) {
                        @tes = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tes_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
		if ($tmd[3] =~ /TND/ ) {
                        @tnd = split ( /$elemDelm/, $cid_item[$item_num][1] );
                        @tnd_tpf = split ( /$elemDelm/, $cid_item[$item_num][2] );
                        next;
                }
	}		
	
#Update inbound_841 table
foreach $p (@po) {
 $dbh->do(" INSERT INTO inbound_841 (    EDI_FILE_ID,
						PO,                             
						REVISION,                             
						ALLOY,                  
						TEMPER,                                 
						MIN_GAUGE,                               
						MAX_GAUGE,                             
						TEI_MIN,                                
						TEI_MAX,                                 
						TBE_MIN,                                
						TBE_MAX,                                 
						TWO_MIN,                                 
						TWO_MAX,                                 
						TPS_MIN,                                
						TPS_MAX,                                 
						TTL_MIN,                                 
						TTL_MAX,                                
						TTL_TPF_MIN,                            
						TTL_TPF_MAX,                             
						TTY_MIN,                                 
						TTY_MAX,                                 
						TTY_TPF_MIN,                             
						TTY_TPF_MAX,                             
						TEL_MIN,                                 
						TEL_MAX,                                 
						TEL_TPF_MIN,                             
						TEL_TPF_MAX,                             
						TNL_MIN,                                 
						TNL_MAX,                                 
						TNL_TPF_MIN,                           
						TNL_TPF_MAX,                            
						TTS_MIN,                                 
						TTS_MAX,                                 
						TTS_TPF_MIN,                             
						TTS_TPF_MAX,                             
						TTT_MIN,                                 
						TTT_MAX,                                 
						TTT_TPF_MIN,                             
						TTT_TPF_MAX,                             
						TET_MIN,                                 
						TET_MAX,                                 
						TET_TPF_MIN,                             
						TET_TPF_MAX,                             
						TNT_MIN,                                 
						TNT_MAX,                                 
						TNT_TPF_MIN,                             
						TNT_TPF_MAX,                             
						TTO_MIN,                                 
						TTO_MAX,                                 
						TTO_TPF_MIN,                             
						TTO_TPF_MAX,                             
						TES_MIN,                                 
						TES_MAX,                                 
						TES_TPF_MIN,                             
						TES_TPF_MAX,                             
						TND_MIN,                                 
						TND_MAX,                                 
						TND_TPF_MIN,                             
						TND_TPF_MAX )	
                                         VALUES ( $file_id,
                                                '$p',     
                                                '$rdt1[4]',     
                                                '$pid1[5]',     
                                                '$pid2[5]',     
                                                '$mea[5]',     
                                                '$mea[6]',     
						'$tei[5]',
						'$tei[6]',
						'$tbe[5]',
						'$tbe[6]',
						'$two[5]',
						'$two[6]',
						'$tps[5]',
						'$tps[6]',
						'$ttl[5]',
						'$ttl[6]',
						'$ttl_tpf[5]',
						'$ttl_tpf[6]',
						'$tty[5]',
                                                '$tty[6]',
                                                '$tty_tpf[5]',
                                                '$tty_tpf[6]',
						'$tel[5]',
                                                '$tel[6]',
                                                '$tel_tpf[5]',
                                                '$tel_tpf[6]',
						'$tnl[5]',
                                                '$tnl[6]',
                                                '$tnl_tpf[5]',
                                                '$tnl_tpf[6]',
						'$tts[5]',
                                                '$tts[6]',
                                                '$tts_tpf[5]',
                                                '$tts_tpf[6]',
						'$ttt[5]',
                                                '$ttt[6]',
                                                '$ttt_tpf[5]',
                                                '$ttt_tpf[6]',
						'$tet[5]',
                                                '$tet[6]',
                                                '$tet_tpf[5]',
                                                '$tet_tpf[6]',
						'$tnt[5]',
                                                '$tnt[6]',
                                                '$tnt_tpf[5]',
                                                '$tnt_tpf[6]',
						'$tto[5]',
                                                '$tto[6]',
                                                '$tto_tpf[5]',
                                                '$tto_tpf[6]',
						'$tes[5]',
                                                '$tes[6]',
                                                '$tes_tpf[5]',
                                                '$tes_tpf[6]',
						'$tnd[5]',
                                                '$tnd[6]',
                                                '$tnd_tpf[5]',
                                                '$tnd_tpf[6]' )
                                        ");
$dbh->do("commit");

} #end of foreache $p



open(MAIL, "/usr/bin/mailx -s 'Alcan EDI 841 received', 'bjiang\@albl\.com' < $INCOMING_841_X12/$X12_file|");
close(MAIL);

} #End of if it is Alcan 841 
}
exit(0);
