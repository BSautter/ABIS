#!/usr/local/bin/perl

# Version history
# 05/23/2002	Bing Jiang	Initial Revison

#File Location
$INCOMING_COIL_856_X12		= "/templar/alcan/incoming_coil_856_x12";
$INCOMING_COIL_856_XML		= "/templar/alcan/incoming_coil_856_xml";
$OUTGOING_COIL_861_XML		= "/templar/alcan/outgoing_coil_861_xml";
$OUTGOING_COIL_861_X12		= "/templar/alcan/outgoing_coil_861_x12";

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

@data_seg = split ( /$segDelm/, "$message" );
#@data_seg_isa = split ( /$elemDelm/, $data_seg[0] );
#@data_seg_gs = split ( /$elemDelm/, $data_seg[1] );


#Make a copy to $INCOMING_COIL_856_X12
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
open (OUT, ">$INCOMING_COIL_856_X12/$X12_file") || die "Cann't create file";
print OUT $lines;
close(OUT);


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
	@st_hls_mea3 = split ( /$elemDelm/, $shipment[6] );
	@st_hls_mea4 = split ( /$elemDelm/, $shipment[7] );
	@st_hls_td1 = split ( /$elemDelm/, $shipment[8] );
	@st_hls_td5 = split ( /$elemDelm/, $shipment[9] );
	@st_hls_td3 = split ( /$elemDelm/, $shipment[10] );
	@st_hls_ref1 = split ( /$elemDelm/, $shipment[11] );
	@st_hls_ref2 = split ( /$elemDelm/, $shipment[12] );
	@st_hls_n11 = split ( /$elemDelm/, $shipment[13] );
	@st_hls_n12 = split ( /$elemDelm/, $shipment[14] );
	@st_hlo = split ( /$elemDelm/, $shipment[15] );
	@st_hlo_lin = split ( /$elemDelm/, $shipment[16] );
	@st_hlo_sn1 = split ( /$elemDelm/, $shipment[17] );
	@st_hlo_prf= split ( /$elemDelm/, $shipment[18] );
	@st_hlo_per = split ( /$elemDelm/, $shipment[19] );

#        print "BOL: $st_hls_ref1[2] \n";

	$item_index = 0;
	for $i ( 20 .. scalar (@shipment)-3) {
		if ($shipment[$i] =~ /^HL\*/)  {
		        push @hl_item, ($item_index ++);
			@{hl_item[$item_index-1]} = ();
        		next;
        		};

	        push (@{$hl_item[$item_index-1]}, $shipment[$i]);
	}
	
	open (FILE, ">${INCOMING_COIL_856_XML}/$st_hls_ref1[2]") or die " Can't create file !!";
		select(FILE);
		print	 "<Shipment> \n";
		print	 "\t<EDI_FILE>$X12_file</EDI_FILE> \n";
		print	 "\t<Gross>$st_hls_mea1[3]</Gross> \n";
		print	 "\t<Net>$st_hls_mea2[3]</Net> \n";
		print    "\t<LN>$st_hls_mea3[3]</LN> \n";
		print	 "\t<TD1>$st_hls_td1[1]</TD1> \n";
		print    "\t<SCAC>$st_hls_td5[3]</SCAC> \n";
	  	print    "\t<Trailer_ID>$st_hls_td3[1]</Trailer_ID> \n";
		print	 "\t<BOL>$st_hls_ref1[2]</BOL> \n";
		print    "\t<PK>$st_hls_ref2[2]</PK> \n";
		print    "\t<Ship_To>$st_hls_n11[2]</Ship_To> \n";
		print    "\t<Ship_From>$st_hls_n12[2]</Ship_From> \n";

		print    "\t<Order>\n";
		print    "\t\t<Part_Number>$st_hlo_lin[3]</Part_Number> \n";
		print    "\t\t<PO>$st_hlo_lin[5]</PO> \n";
		print    "\t\t<Total_Weight>$st_hlo_sn1[2]</Total_Weight> \n";
		print    "\t\t<Contact_Number>$st_hlo_per[4]</Contact_Number> \n";
	
		for $item_num ( 0 .. scalar (@hl_item) -1) {
			@hl_item_pid2	=	split ( /$elemDelm/, $hl_item[$item_num][1] );
			@hl_item_mea1	=	split ( /$elemDelm/, $hl_item[$item_num][7] );
			@hl_item_mea2	=	split ( /$elemDelm/, $hl_item[$item_num][8] );
			@hl_item_mea3	=	split ( /$elemDelm/, $hl_item[$item_num][9] );
			@hl_item_mea4	=	split ( /$elemDelm/, $hl_item[$item_num][10] );
			@hl_item_mea5	=	split ( /$elemDelm/, $hl_item[$item_num][11] );
			@hl_item_mea6	=	split ( /$elemDelm/, $hl_item[$item_num][12] );
			@hl_item_mea7	=	split ( /$elemDelm/, $hl_item[$item_num][13] );
			@hl_item_ref1	=	split ( /$elemDelm/, $hl_item[$item_num][14] );
			@hl_item_ref2	=	split ( /$elemDelm/, $hl_item[$item_num][15] );
			@hl_item_ref3	=	split ( /$elemDelm/, $hl_item[$item_num][16] );
	
			print	"\t\t\t<Coil> \n";	
			print   "\t\t\t\t<Coil_Number>$hl_item_ref3[2]</Coil_Number> \n";
			print   "\t\t\t\t<Temper>$hl_item_pid2[5]</Temper> \n";
			print	"\t\t\t\t<Gross_Weight>$hl_item_mea1[3]</Gross_Weight> \n";
			print	"\t\t\t\t<Net_Weight>$hl_item_mea2[3]</Net_Weight> \n";
			print	"\t\t\t\t<Lineal_Feet>$hl_item_mea3[3]</Lineal_Feet> \n";
			print	"\t\t\t\t<Coil_Width>$hl_item_mea4[3]</Coil_Width> \n";
			print	"\t\t\t\t<Coil_Gauge>$hl_item_mea5[3]</Coil_Gauge> \n";
			print	"\t\t\t\t<Density>$hl_item_mea6[3]</Density> \n";
			print	"\t\t\t\t<LOT>$hl_item_ref1[2]</LOT> \n";
			print	"\t\t\t\t<Pack_ID>$hl_item_ref2[2]</Pack_ID> \n";

			print	"\t\t\t</Coil> \n";	
		
		}
		print "	</Order>\n";
		print "</Shipment>\n";

		select(STDOUT);
	close(FILE);

	#reset @hl_item
	@hl_item = ();
}


open(MAIL, "/usr/bin/mailx -s 'Alcan EDI 856 received', 'bjiang\@albl\.com' < $INCOMING_COIL_856_X12/$X12_file|");
close(MAIL);

} #End of if it is Alcan 856

exit(0);
