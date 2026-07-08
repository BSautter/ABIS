#! /usr/local/bin/perl

# Version history
# File Name: 863_2_db.pl
# 05/01/2003	Bing Jiang	Initial Revison

#File Location
$INCOMING_863_X12               = "/templar/alcan/incoming_863_x12";

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

if ($isa{Sndr} == $ALCAN_DUNS && $gs{FuncId} == "SH" && $st{Type} == "863" ) {

#Make a copy to $INCOMING_863_X12
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
open (OUT, ">$INCOMING_863_X12/$X12_file") || die "Can't create file";
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

	if ($data_seg[$i] =~ /^ST\*863/)  {
	push @st, ($st_index ++);
	next;
	};
	
	push (@{$st[$st_index-1]}, $data_seg[$i]);
}


#foreach $st_line (@st ) {
#	@shipment = @{$st[$st_line]};
#	for $a (0 .. scalar(@shipment)-1) {
#		print (" $st_line : $shipment[$a] \n")
#	}
#}
#exit(1);

foreach $st_line (@st) {
	
	@shipment = @{$st[$st_line]};   # shipment is actually one coil in 863
#       print ("st_line: $st_line \n");

#       for $a (0 .. scalar(@shipment)-1) {
#               print (" $st_line : $shipment[$a] \n")
#       }

	#Get coil# in 863 data
	for $i ( 1 .. scalar(@shipment) - 1) {
		if ($shipment[$i] =~ /^LIN/) {
			@lin = split (/$elemDelm/, $shipment[$i]);
			last;
		}
	}
	#	print ("Coil number: $lin[9] \n");

	$i = 0;

	#Get G N WD and TH 
	while ($shipment[$i] !~ /^CID\*\*54/) {
		$i ++;
#          print ("i: $shipment[$i] \n");
	}
	$i ++;
	@mea1 = split (/$elemDelm/, $shipment[$i ++]);
	@mea2 = split (/$elemDelm/, $shipment[$i ++]);
	@mea3 = split (/$elemDelm/, $shipment[$i ++]);
	@mea4 = split (/$elemDelm/, $shipment[$i ++]);

#          print ( "done mea1-4 \n");

	#Jump to CID*71
	while ($shipment[$i] !~ /^CID\*\*71/) {
                $i ++;
        }

	if ($shipment[$i] !~ /^CID\*\*71/ ) {
		print ("error CID : $shipment[$i]");  # test if $i is correct
		exit(1);
	}

	#Start  handling CID 71
		
	$item_index = 0;
	for $j ( $i .. scalar(@shipment) - 3 ){
		if ($shipment[$j] =~ /^CID\*\*71/)  {
		        push @cid_item, ($item_index ++);
			@{cid_item[$item_index-1]} = ();
        		next;
        		};

	        push (@{$cid_item[$item_index-1]}, $shipment[$j]);
	}


	for $item_num (0 .. scalar (@cid_item) -1) {
		@psd1 = split( /$elemDelm/, $cid_item[$item_num][0] );

		if ($psd1[7] =~ /^23/ ) {
			@tmd = split( /$elemDelm/, $cid_item[$item_num][2] );
                        if ($tmd[3] =~ /TWO/ ) {
                                @two_mea = split ( /$elemDelm/, $cid_item[$item_num][3] );
#print "Lube $two_mea[3] \n";
                                next;
			}


		 }  # end of lube weight

		if ($psd1[7] =~ /^12/ ) {
			@tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
			if ($tmd[3] =~ /TEL/ ) {
                        	@tel_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
				if ( $tel_b_mea1[1] =~ /^TR/ ) {
					@tel_b_mea2 = @tel_b_mea1;
					@tel_b_mea1 = (); 		
                        		next;
				}
				@tel_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
				next;		

               		} 

			if ($tmd[3] =~ /ITH/ ) {
                                @ith_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ith_b_mea1[1] =~ /^TR/ ) {
                                        @ith_b_mea2 = @ith_b_mea1;
                                        @ith_b_mea1 = ();               
                                        next;
                                }
                                @ith_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;           

                        } 

			if ($tmd[3] =~ /TTU/ ) {
                                @ttu_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttu_b_mea1[1] =~ /^TR/ ) {
                                        @ttu_b_mea2 = @ttu_b_mea1;
                                        @ttu_b_mea1 = ();               
                                        next;
                                }
                                @ttu_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;           

                        } 

			 if ($tmd[3] =~ /TNL/ ) {
                                @tnl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnl_b_mea1[1] =~ /^TR/ ) {
                                        @tnl_b_mea2 = @tnl_b_mea1;
                                        @tnl_b_mea1 = ();
                                        next;
                                }
                                @tnl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

			 if ($tmd[3] =~ /TRL/ ) {
                                @trl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trl_b_mea1[1] =~ /^TR/ ) {
                                        @trl_b_mea2 = @trl_b_mea1;
                                        @trl_b_mea1 = ();
                                        next;
                                }
                                @trl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }



			if ($tmd[3] =~ /TTY/ ) {
                                @tty_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tty_b_mea1[1] =~ /^TR/ ) {
                                        @tty_b_mea2 = @tty_b_mea1;
                                        @tty_b_mea1 = ();               
                                        next;
                                }
                                @tty_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;           

                        } 

		}  # end of backend


# Starting  Frontend 

		if ($psd1[7] =~ /^11/ ) {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][2] );
                        if ($tmd[3] =~ /TEL/ ) {
                                @tel_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tel_f_mea1[1] =~ /^TR/ ) {
                                        @tel_f_mea2 = @tel_f_mea1;
                                        @tel_f_mea1 = ();
                                        next;
                                }
                                @tel_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;
                        }

                        if ($tmd[3] =~ /ITH/ ) {
                                @ith_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $ith_f_mea1[1] =~ /^TR/ ) {
                                        @ith_f_mea2 = @ith_f_mea1;
                                        @ith_f_mea1 = ();
                                        next;
                                }
                                @ith_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;
                        }

                        if ($tmd[3] =~ /TTU/ ) {
                                @ttu_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $ttu_f_mea1[1] =~ /^TR/ ) {
                                        @ttu_f_mea2 = @ttu_f_mea1;
                                        @ttu_f_mea1 = ();               
                                        next;
                                }
                                @ttu_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        } 

			if ($tmd[3] =~ /TTY/ ) {
                                @tty_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tty_f_mea1[1] =~ /^TR/ ) {
                                        @tty_f_mea2 = @tty_f_mea1;
                                        @tty_f_mea1 = ();               
                                        next;
                                }
                                @ttu_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }
                        
			if ($tmd[3] =~ /TNL/ ) {
                                @tnl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tnl_f_mea1[1] =~ /^TR/ ) {
                                        @tnl_f_mea2 = @tnl_f_mea1;
                                        @tnl_f_mea1 = ();               
                                        next;
                                }
                                @tnl_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }
				
			if ($tmd[3] =~ /TRL/ ) {
                                @trl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $trl_f_mea1[1] =~ /^TR/ ) {
                                        @trl_f_mea2 = @trl_f_mea1;
                                        @trl_f_mea1 = ();               
                                        next;
                                }
                                @trl_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TET/ ) {
                                @tet_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tet_f_mea1[1] =~ /^TR/ ) {
                                        @tet_f_mea2 = @tet_f_mea1;
                                        @tet_f_mea1 = ();               
                                        next;
                                }
                                @tet_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TTL/ ) {
                                @ttl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $ttl_f_mea1[1] =~ /^TR/ ) {
                                        @ttl_f_mea2 = @ttl_f_mea1;
                                        @ttl_f_mea1 = ();               
                                        next;
                                }
                                @ttu_l_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TTT/ ) {
                                @ttt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $ttt_f_mea1[1] =~ /^TR/ ) {
                                        @ttt_f_mea2 = @ttt_f_mea1;
                                        @ttt_f_mea1 = ();               
                                        next;
                                }
                                @ttt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }
		
			if ($tmd[3] =~ /TNT/ ) {
                                @tnt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tnt_f_mea1[1] =~ /^TR/ ) {
                                        @tnt_f_mea2 = @tnt_f_mea1;
                                        @tnt_f_mea1 = ();               
                                        next;
                                }
                                @tnt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TRT/ ) {
                                @trt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $trt_f_mea1[1] =~ /^TR/ ) {
                                        @trt_f_mea2 = @trt_f_mea1;
                                        @trt_f_mea1 = ();               
                                        next;
                                }
                                @trt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TES/ ) {
                                @tes_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tes_f_mea1[1] =~ /^TR/ ) {
                                        @tes_f_mea2 = @tes_f_mea1;
                                        @tes_f_mea1 = ();               
                                        next;
                                }
                                @tes_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TTS/ ) {
                                @tts_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tts_f_mea1[1] =~ /^TR/ ) {
                                        @tts_f_mea2 = @tts_f_mea1;
                                        @tts_f_mea1 = ();               
                                        next;
                                }
                                @tts_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TTO/ ) {
                                @tto_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tto_f_mea1[1] =~ /^TR/ ) {
                                        @tto_f_mea2 = @tto_f_mea1;
                                        @tto_f_mea1 = ();               
                                        next;
                                }
                                @tto_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TND/ ) {
                                @tnd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $tnd_f_mea1[1] =~ /^TR/ ) {
                                        @tnd_f_mea2 = @tnd_f_mea1;
                                        @tnd_f_mea1 = ();               
                                        next;
                                }
                                @tnd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }

			if ($tmd[3] =~ /TRD/ ) {
                                @trd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                if ( $trd_f_mea1[1] =~ /^TR/ ) {
                                        @trd_f_mea2 = @trd_f_mea1;
                                        @trd_f_mea1 = ();               
                                        next;
                                }
                                @trd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][4] );
                                next;           
                        }


		}  # end of front end



 }		

	
	# Starting handle CID**68
	while ($shipment[$i] !~ /CID\*\*68/) {
                $i ++;
        }
	$i ++;

	while  ($shipment[$i] !~ /SE/ ) {
		@mea = split (/$elemDelm/, $shipment[$i ++]);
		if ($mea[2] =~ /ZSI/) {
			$zsi = $mea[3];
		}
		if ($mea[2] =~ /ZFE/) {
                        $zfe = $mea[3];
                }
		if ($mea[2] =~ /ZCU/) {
                        $zcu = $mea[3];
                }
		if ($mea[2] =~ /ZMN/) {
                        $zmn = $mea[3];
                }
		if ($mea[2] =~ /ZMG/) {
                        $zmg = $mea[3];
                }
		if ($mea[2] =~ /ZCR/) {
                        $zcr = $mea[3];
                }
		if ($mea[2] =~ /ZNI/) {
                        $zni = $mea[3];
                }
		if ($mea[2] =~ /ZZN/) {
                        $zzn = $mea[3];
                }
		if ($mea[2] =~ /ZTI/) {
                        $zti = $mea[3];
                }
		if ($mea[2] =~ /BB/) {
                        $bb = $mea[3];
                }
		if ($mea[2] =~ /ZAL/) {
                        $zal = $mea[3];
		}
		if ($mea[2] =~ /GH/) {
                        $zgh = $mea[3];
                }
		if ($mea[2] =~ /V/) {
			$v = $mea[3];
		}
 	}
#print ("zmg: $zmg\n");
	
#Update inbound_863 table

 $dbh->do(" INSERT INTO inbound_863 (    EDI_FILE_ID,
						coil_num,                             
						WD,                             
						TH,                  
						TWO,
						TTY_F_M1,                                 
						TTY_F_M2,                               
						TTU_F_M1,                             
						TTU_F_M2,                             
						TEL_F_M1,
						TEL_F_M2,
						TRL_F_M1,	
						TRL_F_M2,	
						TNL_F_M1,
                                                TNL_F_M2,
						TTY_B_M1,
                                                TTY_B_M2,
						TTU_B_M1,
                                                TTU_B_M2,			
						TEL_B_M1,
                                                TEL_B_M2,
						TRL_B_M1,
                                                TRL_B_M2,
						TNL_B_M1,
                                                TNL_B_M2,
						TTO_F_M1,
                                                TTO_F_M2,
						TTS_F_M1,
                                                TTS_F_M2,
						TES_F_M1,
                                                TES_F_M2,
						TRD_F_M1,
                                                TRD_F_M2,
						TND_F_M1,
                                                TND_F_M2,
						TTT_F_M1,
                                                TTT_F_M2,
						TTL_F_M1,
                                                TTL_F_M2,
						TET_F_M1,
                                                TET_F_M2,
						TRT_F_M1,
                                                TRT_F_M2,
						TNT_F_M1,
                                                TNT_F_M2,
						SI,
                                                FE,
						CU,
						MN,
						MG,
						CR,
						NI,
						ZN,
						TI,
						GH,
						AL,
						BB,
						V )
                                         VALUES ( $file_id,
                                                '$lin[9]',     
                                                '$mea3[3]',     
                                                '$mea4[3]',     
						'$two_mea[3]',
                                                '$tty_f_mea1[3]',     
                                                '$tty_f_mea2[3]',     
                                                '$ttu_f_mea1[3]',     
						'$ttu_f_mea2[3]',
						'$tel_f_mea1[3]',
						'$tel_f_mea2[3]',
						'$trl_f_mea1[3]',
						'$trl_f_mea2[3]',
						'$tnl_f_mea1[3]',
						'$tnl_f_mea2[3]',
						'$tty_b_mea1[3]',
						'$tty_b_mea2[3]',
						'$ttu_b_mea1[3]',
						'$ttu_b_mea2[3]',
						'$tel_b_mea1[3]',
						'$tel_b_mea2[3]',
                                                '$trl_b_mea1[3]',
                                                '$trl_b_mea2[3]',
                                                '$tnl_b_mea1[3]',
						'$tnl_b_mea2[3]',
                                                '$tto_f_mea1[3]',
                                                '$tto_f_mea2[3]',
                                                '$tts_f_mea1[3]',
						'$tts_f_mea2[3]',
                                                '$tes_f_mea1[3]',
                                                '$tes_f_mea2[3]',
                                                '$trd_f_mea1[3]',
						'$trd_f_mea2[3]',
                                                '$tnd_f_mea1[3]',
                                                '$tnd_f_mea2[3]',
                                                '$ttt_f_mea1[3]',
						'$ttt_f_mea2[3]',
                                                '$ttl_f_mea1[3]',
                                                '$ttl_f_mea2[3]',
                                                '$tet_f_mea1[3]',
						'$tet_f_mea2[3]',
                                                '$trt_f_mea1[3]',
                                                '$trt_f_mea2[3]',
                                                '$tnt_f_mea1[3]',
						'$tnt_f_mea2[3]',
                                                '$zsi',
                                                '$zfe',
                                                '$zcu',
						'$zmn',
                                                '$zmg',
                                                '$zcr',
                                                '$zni',
						'$zzn',
                                                '$zti',
                                                '$zgh',
                                                '$zal',
						'$bb',
						'$v')
                                        ");
$dbh->do("commit");
$lin[9] = ();
$mea3[3] = ();
$mea4[3] = ();
$two_mea[3] = ();
$tty_f_mea1[3] = ();
$tty_f_mea2[3] = ();
$ttu_f_mea1[3] = ();
$ttu_f_mea2[3] = ();
$tel_f_mea1[3] = ();
$tel_f_mea2[3] = ();
$trl_f_mea1[3] = ();
$trl_f_mea2[3] = ();
$tnl_f_mea1[3] = ();
$tnl_f_mea2[3] = ();
$tty_b_mea1[3] = ();
$tty_b_mea2[3] = ();
$ttu_b_mea1[3] = ();
$ttu_b_mea2[3] = ();
$tel_b_mea1[3] = ();
$tel_b_mea2[3] = ();
$trl_b_mea1[3] = ();
$trl_b_mea2[3] = ();
$tnl_b_mea1[3] = ();
$tnl_b_mea2[3] = ();
$tto_f_mea1[3] = ();
$tto_f_mea2[3] = ();
$tts_f_mea1[3] = ();
$tts_f_mea2[3] = ();
$tes_f_mea1[3] = ();
$tes_f_mea2[3] = ();
$trd_f_mea1[3] = ();
$trd_f_mea2[3] = ();
$tnd_f_mea1[3] = ();
$tnd_f_mea2[3] = ();
$ttt_f_mea1[3] = ();
$ttt_f_mea2[3] = ();
$ttl_f_mea1[3] = ();
$ttl_f_mea2[3] = ();
$tet_f_mea1[3] = ();
$tet_f_mea2[3] = ();
$trt_f_mea1[3] = ();
$trt_f_mea2[3] = ();
$tnt_f_mea1[3] = ();
$tnt_f_mea2[3] = ();
$zsi = ();
$zfe = ();
$zcu = ();
$zmn = ();
$zmg = ();
$zcr = ();
$zni = ();
$zzn = ();
$zti = ();
$zgh = ();
$zal = ();
$bb = ();
$v = ();

#print (" coil $lin[9] done \n");
}  #End of one coil

} #End of if it is Alcan 863

open(MAIL, "/usr/bin/mailx -s 'Alcan EDI 863 received', 'vhuang\@albl\.com' < $INCOMING_863_X12/$X12_file|");
close(MAIL);

#print ("863 done\n");
exit(0);
