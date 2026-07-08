#! /usr/local/bin/perl

# Version history
# File Name: 863_2_db.pl
# 05/01/2003    Bing Jiang      Initial Revison

#File Location
$INCOMING_863_X12               = "/templar/alcan/incoming_863_x12";

#$INCOMING_863_X12               = "/templar/templar/receive/edi/ONLY_863s/alex";
#$INCOMING_863_X12 = "/export/home/oracle11g/edi/receive/VanDocuments";

#Set up DBI environment

use DBI;
$ENV{ORACLE_SID} = 'abc11';
$ENV{ORACLE_HOME} ='/apps/oracle/product/9.2.0.1.0';


eval 'use Oraperl; 1' || die $@ if $] >= 5;

#$db='dbi:Oracle:host=localhost;sid=abc11;port=1523';             #Generic
$db='dbi:Oracle:host=192.168.1.9;sid=abc11;port=1523';           #Production
# $db='dbi:Oracle:host=192.168.1.11;sid=abc11;port=1523';          #Development

$dbh = DBI->connect( $db, 'dbo/__DB_PASSWORD_REDACTED__');

if (!defined $dbh) { die "Cannot to \$dbh->connect: $DBI::errstr\n"; }


=pod
$query = "select email_address from auto_report_emails where report_name = 'hold for cert email'";

$cth = $dbh->prepare($query);
$cth->execute();

while (  $addr = $cth->fetchrow() ) {
    print "addr: $addr   \n";
}
=cut


#Get file id from Oracle sequence object
$sth2 = $dbh->prepare ( "SELECT edi_inbound_file_id_seq.NEXTVAL from dual");
$sth2->execute;
$file_id = $sth2->fetchrow();

#$dbh->do(" INSERT INTO inbound_transaction (EDI_FILE_ID,EDI_FILE_NAME) VALUES ( $file_id, '23232.222' )");

#Get date/time from Oracle
$sth3 = $dbh->prepare ( "select to_char(current_date, 'mm/dd/yyyy hh24:mi:ss') from dual");
$sth3->execute;
$dt = $sth3->fetchrow();

$CONSTELLIUM_DUNS     =       "043207177";   #Production
# $CONSTELLIUM_DUNS     =       "043207177";         #Development
$ABCO_DUNS      =       "039630926T";

$edi_file = $ARGV[0];
%isa = ();
%gs = ();
%st = ();

open (FILE,"<$edi_file") or die "Couldn't Open Incoming File: $!\n ";
while ( $x = <FILE> ) { $message .= $x; }
close(FILE);

$message =~ s/@//g; #Remove @

# Set-up delimeters within the EDI data
        $elem = substr( $message, 103, 1 );
          $elemDelm = quotemeta $elem;          # Element Separator
        $sub = substr( $message, 104, 1 );
        $sub = $elem; #Alex Gerlants. 08/15/2019
          $subDelm = quotemeta $sub;            # Sub Element Separator
        $seg = substr( $message, 104, 1 );
          $segDelm = quotemeta $seg;            # Segment Separator


$message_nospaces = $message;

$message_nospaces =~ s/ //g; #Remove spaces from $message

#$message_nospaces =~ s/@//g; #Remove @

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

# print ("isa{Sndr}: $isa{Sndr}    gs{FuncId}: $gs{FuncId}     st{Type}: $st{Type}     edi_file: $edi_file\n\n");

if ($st{Type} ne "863") {
   exit(0);
}

$ok_997 = "no";

if ($isa{Sndr} == $CONSTELLIUM_DUNS && $gs{FuncId} == "RT" && $st{Type} == "863" ) {

   $ok_997 = "yes";

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

# print "Before Update inbound_transaction table. EDI file_id: $file_id  \n";

#Update inbound_transaction table
$dbh->do(" INSERT INTO inbound_transaction (    EDI_FILE_ID,
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

#--------------------------------------------------------------------------------------------------------
#Scan the file to see if it has no chemicals and/or no mechanicals
#If the file has no chemicals and/or no mechanicals, send email to external and internal users, and exit
#--------------------------------------------------------------------------------------------------------

my $chem_count = 0;
my $mech_count = 0;

my $bol = "";
my $ind_ref_si = -1;
my $length_ref_si = 0;
my $length_data_line = 0;

for $i (2 ..  scalar(@data_seg)-3 ) {
   # print "data_seg[$i]: $data_seg[$i]  \n";

   $data_line = $data_seg[$i];

#print "\ndata_line: $data_line\ndata_line[0]: $data_line[0]  data_line[1]: $data_line[1] data_line[2]: $data_line[2] data_line[4]: $
#data_line[4] data_line[5]: $data_line[5] \n";

   if ( $bol eq "" ) {
       $ind_ref_si = index($data_line, "REF~SI~", 0); # Start from the beginning of $data_line

       if ( $ind_ref_si == 0 ) {
           $length_data_line = length($data_line);
           $length_ref_si = length( "REF~SI~");
           $bol = substr($data_line, $length_ref_si,  $length_data_line - $length_ref_si + 1);
           # print "length_data_line: $length_data_line  length_ref_si: $length_ref_si  bol: $bol  \n";
       }
   }

   $result_chem = index($data_line, "MEA~CH");
   # print "result_chem: $result_chem  \n";

   if ( $result_chem == 0 ) {
      # print "Inside result_chem = 0  \n";
      $chem_count++;
   }

   $result_mech = index($data_line, "MEA~TR");
   # print "result_mech: $result_mech  \n";

   if ( $result_mech == 0 ) {
      # print "Inside result_mech = 0  \n";
      $mech_count++;
   }

   if ($data_seg[$i] =~ /^ST\~863/)  {
      push @st, ($st_index ++);
      next;
   };

   push (@{$st[$st_index-1]}, $data_seg[$i]);
} #for $i (2 ..  scalar(@data_seg)-3 )

# print "Before TEST ONLY. chem_ceunt: $chem_count  mech_count: $mech_count  \n";

#$chem_count = 0; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
#$mech_count = 0; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

# print "After TEST ONLY. chem_count: $chem_count  mech_count: $mech_count  \n";

my $message = "";
my $message2 = "";

if ( $chem_count <= 0 || $mech_count <= 0 ) {
#   print "Inside if ( chem_count <= 0 || mech_count <= 0 )  \n";
   #Send email


   if ( $chem_count <= 0 && $mech_count <= 0 ) {
        $message = "Constellium 863 file $edi_file. Chemical and mechanical test results missing.";
        $message2 = "CHEMICAL TEST RESULTS MISSING|MECHANICAL TEST RESULTS MISSING";
   }
   elsif ( $chem_count <= 0 ) {
       $message = "Constellium 863 file $edi_file. Chemical test results missing.";
       $message2 = "CHEMICAL TEST RESULTS MISSING";
   }
   else {
       $message = "Constellium 863 file $edi_file. Mechanical test results missing.";
       $message2 = "MECHANICAL TEST RESULTS MISSING";
   }

   $subject = "ALBL 863 file $edi_file. Chemical and/or mechanical test results missing";
   $from = 'ALBL 863 receive';
   $query = "select email_address from auto_report_emails where report_name = '863 test results missing'";
   my $addr = "";

   $dth = $dbh->prepare($query);
   $dth->execute();

   while (  $addr = $dth->fetchrow() ) {
       # print "addr: $addr   \n";
       $to = $addr;

       # $addr = "agerlant@hotmail.com";
       # print "addr: $addr   \n";

       my $param = "'$from', '$to', '$subject', '$message'";
#       print "param 863 test results missing email: $param  \n";
       my $cmd = "/templar/templar/util/send_email.pl $param";
#       print "cmd hold for cert email: $cmd  \n";
       my $result = system($cmd);
   }

   # Create 824 file
   $param = "'$edi_file' '$bol' '$message2'";   
   $cmd = "/templar/templar/util/824_const.pl $param";
   $result = system($cmd);

   exit(0);
}

$char_code = "";
$sample_desc = "";
$test_method = "";
# my $bol = "";

foreach $st_line (@st ) {
       @shipment = @{$st[$st_line]};

       for $a (0 .. scalar(@shipment)-1) {
          @line = split (/$elemDelm/, $shipment[$a]);

#          if ( uc($line[0]) eq "REF" && uc($line[1]) eq "SI" ) {
#             $bol = $line[2];
#             print "bol: $bol  \n";
#          };

          $tmd = uc($line[0]);

          if (uc($line[0]) =~ /LIN/) {
             $coil_num = $line[13];
#             print "Inside LIN coil_num: $coil_num \n";
          };

          if ( uc($line[0]) eq "TMD" && uc($line[3]) eq "TCF" ) {
             $chemical_test_date = $line[7]
          };

          if ( uc($line[0]) eq "TMD" && uc($line[3]) eq "ZLT" ) {
             $lube_date = $line[7]
          };

          #print "tmd: $tmd   tmd_prev: $tmd_prev \n";

          if ( $tmd eq "MEA" && $tmd_prev eq "ZLT") {
             $lube_weight = $line[3];
             $lube_weight_uom = $line[4];
          }

          $tmd_prev = uc($line[3]);

          if ( uc($line[0]) eq "PID" && $line[2] eq "38" ) {
             $grade = $line[4];
          };

          if (uc($line[0]) =~ /CID/) {
             $char_code = "";

             if ( $line[2] =~ /54/ ) {
                $char_code = "section_profile";
             };

             if ( $line[2] =~ /71/ ) {
                $char_code = "mechanical";
             };

             if ( $line[2] =~ /68/ ) {
                $char_code = "chemistry";
             };
          };

#-----------------------------------------------------------------------------------------------------


             if (uc($line[0]) eq "MEA" && $line[1] eq "TR" && $line[2] eq "YB") {
                $ttt_b_mea2[3] = $line[3];
                $ttt_f_mea2[3] = $line[3];
                $ttt_b_mea2[4] = $line[4];
                $ttt_f_mea2[4] = $line[4];
             }

             if (uc($line[0]) eq "MEA" && $line[1] eq "TR" && $line[2] eq "TF") {
                $ttl_b_mea2[3] = $line[3];
                $ttl_f_mea2[3] = $line[3];
                $ttl_b_mea2[4] = $line[4];
                $ttl_f_mea2[4] = $line[4];
             }

             if (uc($line[0]) eq "MEA" && $line[1] eq "TR" && $line[2] eq "EA") {
                $tet_b_mea2[3] = $line[3];
                $tet_f_mea2[3] = $line[3];
                $tet_b_mea2[4] = $line[4];
                $tet_f_mea2[4] = $line[4];
             }

             if (uc($line[0]) eq "MEA" && $line[1] eq "TR" && $line[2] eq "MR") {
                $trt_b_mea2[3] = $line[3];
                $trt_f_mea2[3] = $line[3];
                $trt_b_mea2[4] = $line[4];
                $trt_f_mea2[4] = $line[4];
             }
#-----------------------------------------------------------------------------------------------------


          if (uc($line[0]) eq "MEA" &&  $char_code eq "section_profile") {
             if (uc($line[1]) eq "WT") {
                #$net_weight = $line[3] . '|' . $line[4];
                $net_weight = $line[3];
             };

             if (uc($line[1]) eq "PD" && uc($line[2]) eq "WD") {
                #$width = $line[3] / 25.4; #Convert to inches
                #$width = $line[3] . '|' . $line[4];
                $width = $line[3];
             };

             if (uc($line[1]) eq "PD" && uc($line[2]) eq "TH") {
                #$thickness = $line[3] / 25.4; #Convert to inches
                #$thickness = $line[3] . '|' . $line[4];
                $thickness = $line[3];
                $char_code = "";
             };
          };

          if ($char_code eq "mechanical") {
             $sample_desc = "";

             if (uc($line[0]) eq "PSD" && $line[7] eq "11") {
                $sample_desc = "front";
             }
             elsif (uc($line[0]) eq "PSD" && $line[7] eq "12") {
                $sample_desc = "back";
             }
             else {
                $sample_desc = "";
             };

             #$char_code = "";

          };

          if ($char_code eq "chemistry") {
             $char_code = "";
          };


       }
}
#exit(1);


#print "line: @line \n";

foreach $st_line (@st) {

        @shipment = @{$st[$st_line]};   # shipment is actually one coil in 863

#       for $a (0 .. scalar(@shipment)-1) {
#               print (" $st_line : $shipment[$a] \n")
#       }

        #Get coil# in 863 data
        for $i ( 1 .. scalar(@shipment) - 1) {

                # $i = 20 here ***********************************************************************
                if (uc($shipment[$i]) =~ /^LIN/) {
                        @lin = split (/$elemDelm/, $shipment[$i]);
                        last;
                }
        }

        $i = 0;

        #Get Process Date/Time in 863 data
        for $i ( 1 .. scalar(@shipment) - 1) {
#                $first_3char = substr($shipment[$i], 0, 3);

=pod
                if ( $first_3char eq "CTT" ) {
           print "Inside IF last  \n";
                    last;
                }
=cut

                if (uc($shipment[$i]) =~ /^DTM/){ 
                        @pr = split (/$elemDelm/, $shipment[$i]);

                        if ($pr[1] eq '009') { 
                           $proc_date = $pr[2]; 
                           $proc_time = $pr[3];
                           #last;
                        } 

                        if ($pr[1] eq '405') {
                           $prod_date = $pr[2];
                           $prod_time = $pr[3];
                           #last;
                        }

                        if ($pr[1] eq '936') {
                           $cash_date = $pr[2];
                           #last;
                        }

                        if ($pr[1] eq '950') {
                           $lube_date = $pr[2];
                           #last;
                        }

                        if ($pr[1] eq '011') {
                           $ship_date = $pr[2];
                           $ship_time = $pr[3];
                           #last;
                        }

               } 

                if (uc($shipment[$i]) =~ /^MEA/){
                        @chem = split (/$elemDelm/, $shipment[$i]);

                        if (uc($chem[2]) eq 'ZAL') {
                           #$zal = $chem[3] . '|' . $chem[4];
                           $zal = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZCR') {
                           #$zcr = $chem[3] . '|' . $chem[4];
                           $zcr = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZCU') {
                           #$zcu = $chem[3] . '|' . $chem[4];
                           $zcu = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZFE') {
                           #$zfe = $chem[3] . '|' . $chem[4];
                           $zfe = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZLB') {
                           #$zlb = $chem[3] . '|' . $chem[4];
                           $zlb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZLN') {
                           #$zln = $chem[3] . '|' . $chem[4];
                           $zln = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZLT') {
                           #$zlt = $chem[3] . '|' . $chem[4];
                           $zlt = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZLX') {
                           #$zlx = $chem[3] . '|' . $chem[4];
                           $zlx = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZMG') {
                           #$zmg = $chem[3] . '|' . $chem[4];
                           $zmg = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZMN') {
                           #$zmn = $chem[3] . '|' . $chem[4];
                           $zmn = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZNI') {
                           #$zni = $chem[3] . '|' . $chem[4];
                           $zni = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZSI') {
                           #$zsi = $chem[3] . '|' . $chem[4];
                           $zsi = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZTI') {
                           #$zti = $chem[3] . '|' . $chem[4];
                           $zti = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZZN') {
                           #$zzn = $chem[3] . '|' . $chem[4];
                           $zzn = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZB') {
                           #$zb = $chem[3] . '|' . $chem[4];
                           $zb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZCA') {
                           #$zca = $chem[3] . '|' . $chem[4];
                           $zca = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZCD') {
                           #$zcd = $chem[3] . '|' . $chem[4];
                           $zcd = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'LB') {
                           #$lb = $chem[3] . '|' . $chem[4];
                           $lb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'SX') {
                           #$sx = $chem[3] . '|' . $chem[4];
                           $sx = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZPB') {
                           #$zpb = $chem[3] . '|' . $chem[4];
                           $zpb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZSB') {
                           #$zsb = $chem[3] . '|' . $chem[4];
                           $zsb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZSN') {
                           #$zsn = $chem[3] . '|' . $chem[4];
                           $zsn = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZZR') {
                           #$zzr = $chem[3] . '|' . $chem[4];
                           $zzr = $chem[3];
                           #last;
                        }


                        if (uc($chem[2]) eq 'ZAS') {
                           #$zas = $chem[3] . '|' . $chem[4];
                           $zas = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZBI') {
                           #$zbi = $chem[3] . '|' . $chem[4];
                           $zbi = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZB') {
                           #$zb = $chem[3] . '|' . $chem[4];
                           $zb = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZV') {
                           #$zv = $chem[3] . '|' . $chem[4];
                           $zv = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZCO') {
                           #$zco = $chem[3] . '|' . $chem[4];
                           $zco = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'IK') {
                           #$ik = $chem[3] . '|' . $chem[4];
                           $ik = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'ZP') {
                           #$zp = $chem[3] . '|' . $chem[4];
                           $zp = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'SS') {
                           #$ss = $chem[3] . '|' . $chem[4];
                           $ss = $chem[3];
                           #last;
                        }

                        if (uc($chem[2]) eq 'SH') {
                           #$sh = $chem[3] . '|' . $chem[4];
                           $sh = $chem[3];
                           #last;
                        }

                        #if (uc($chem[2]) eq 'ZV') {
                        #   $zv = $chem[3] . '|' . $chem[4];
                        #   #last;
                        #}

                        if (uc($chem[2]) eq 'BB') {
                           #$bb = $chem[3] . '|' . $chem[4];
                           $bb = $chem[3];
                        }

                        if (uc($chem[2]) eq 'GH') {
                           #$zgh = $chem[3] . '|' . $chem[4];
                           $zgh = $chem[3];
                        }

                        if (uc($chem[2]) eq 'ZV') {
                           #$v = $chem[3] . '|' . $chem[4];
                           $v = $chem[3];
                        }

                }

        } # #Get Process Date/Time in 863 data

=pod
        #Get G N WD and TH
        while ($shipment[$i] !~ /^CID\*\*54/) {
           $i ++;
           print "Inside 'Get G N WD and TH'.  i: $i   shipment[$i]: $shipment[$i]  \n";
        }
=cut

        # $i = 25 here ***********************************************************************

        #$i ++;

        @mea1 = split (/$elemDelm/, $shipment[$i ++]);
        @mea2 = split (/$elemDelm/, $shipment[$i ++]);
        @mea3 = split (/$elemDelm/, $shipment[$i ++]);
        @mea4 = split (/$elemDelm/, $shipment[$i ++]);

        #Start  handling CID 71

#print "Printing shipment[] Begin \n";
#foreach (@shipment) {
#    print "$_\n";
#};
#print "Printing shipment[] End \n";

        #$i --;

#print "Before 'item_index = 0' i: $i   scalar(shipment): scalar(@shipment) \n\n\n";

#####################################################################################################################

        $item_index = 0;
        #for $j ($i .. ( scalar(@shipment) - 3 ) ){
        for $j ($i .. ( scalar(@shipment) ) ){


           #if (substr($shipment[$j], 1, 3) eq "SE*" ) {
           #   last;
           #}

           #print "j: $j   i: $i   item_index: $item_index   shipment[$j]: $shipment[$j]  \n";

           if (uc($shipment[$j]) =~ /^CID\*\*71/)  {


              #print "item_index: $item_index    shipment[$j]: $shipment[$j]  \n";

              push @cid_item, ($item_index ++);

              if ( $item_index gt 0 ) {
                 @{cid_item[$item_index-1]} = ();
              }

              next;
           };
             
           #print "j: $j   i: $i   item_index: $item_index   shipment[$j]: $shipment[$j]  \n";

           if ( $item_index gt 0 ) {
              push (@{$cid_item[$item_index-1]}, $shipment[$j]);
           }
        }


        #for $item_num (0 .. scalar (@cid_item) - 1) {
        for $item_num (0 .. scalar (@cid_item)) {

           if (uc(substr($cid_item[$item_num], 1, 3)) eq "SE*" ) {
              last;
           }

                @psd1 = split( /$elemDelm/, $cid_item[$item_num][0] );

#print "          psd1[1]: $psd1[1]   psd1[2]: $psd1[2]   psd1[3]: $psd1[3]   psd1[4]: $psd1[4]   psd1[5]: $psd1[5]   psd1[6]: $psd1[6]    psd1[7]: $psd1[7]  \n";

                if ($psd1[7] =~ /^23/ ) {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][2] );
                        if ($tmd[3] =~ /TWO/ ) {
                                @two_mea = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                #print "Lube $two_mea[3] \n";
                                next;
                        }


                 }  # end of lube weight

                if (uc($psd1[3]) eq "ZLT") {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
                        #print "           tmd[1]: $tmd[1]  tmd[2]: $tmd[2]  tmd[3]: $tmd[3]  tmd[4]: $tmd[4]  tmd[5]: $tmd[5]  tmd[6]: $tmd[6]  tmd[7]: $tmd[7]  tmd[8]: $tmd[8]  tmd[9]: $tmd[9]  \n";
                        $zlt = $tmd[3] . '|' . $tmd[4];
                        #print "zlt: $zlt  \n";
                }

                if (uc($psd1[3]) eq "ZLB") {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
                        #print "           tmd[1]: $tmd[1]  tmd[2]: $tmd[2]  tmd[3]: $tmd[3]  tmd[4]: $tmd[4]  tmd[5]: $tmd[5]  tmd[6]: $tmd[6]  tmd[7]: $tmd[7]  tmd[8]: $tmd[8]  tmd[9]: $tmd[9]  \n";
                        $zlb = $tmd[3];
                        #print "zlb: $zlb  \n";
                }

                if (uc($psd1[3]) eq "ZLN") {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
                        #print "           tmd[1]: $tmd[1]  tmd[2]: $tmd[2]  tmd[3]: $tmd[3]  tmd[4]: $tmd[4]  tmd[5]: $tmd[5]  tmd[6]: $tmd[6]  tmd[7]: $tmd[7]  tmd[8]: $tmd[8]  tmd[9]: $tmd[9]  \n";
                        $zln = $tmd[3];
                        #print "zln: $zln  \n";
                }

                if (uc($psd1[3]) eq "ZLX") {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
                        #print "           tmd[1]: $tmd[1]  tmd[2]: $tmd[2]  tmd[3]: $tmd[3]  tmd[4]: $tmd[4]  tmd[5]: $tmd[5]  tmd[6]: $tmd[6]  tmd[7]: $tmd[7]  tmd[8]: $tmd[8]  tmd[9]: $tmd[9]  \n";
                        $zlx = $tmd[3];
                        #print "zlx: $zlx  \n";
                }

#-------------------------------------------------------------------------------------------------------
=pod
=cut
#-------------------------------------------------------------------------------------------------------

# Starting  Backend

=pod
                if ($psd1[7] =~ /^12/ ) {
                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );

                        if (uc($tmd[3]) =~ /TEL/ ) {
                                @tel_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tel_b_mea1[1] =~ /^TR/ ) {
                                        @tel_b_mea2 = @tel_b_mea1;
                                        $tel_b_mea2[3] = $tel_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tel_b_mea1 = ();
                                        next;
                                }
                                @tel_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                        #print "Before ITH Back. tmd[3]: $tmd[3]  \n";

                        if (uc($tmd[3]) =~ /ITH/ ) {

                                #print "Inside ITH Back  \n";

                                @ith_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ith_b_mea1[1] =~ /^TR/ ) {
                                        @ith_b_mea2 = @ith_b_mea1;
                                        $ith_b_mea2[3] = $ith_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ith_b_mea1 = ();
                                        next;
                                }
                                @ith_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                        #print "tmd[3]: $tmd[3]  \n";

                        if (uc($tmd[3]) =~ /TTU/ ) {
                                @ttu_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );

                                #print "ttu_b_mea1[1]: $ttu_b_mea1[1]  \n";

                                if ( uc($ttu_b_mea1[1]) =~ /^TR/ ) {
                                        @ttu_b_mea2 = @ttu_b_mea1;
                                        $ttu_b_mea2[3] = $ttu_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ttu_b_mea1 = ();
                                        #print "ttu_b_mea1[1]: $ttu_b_mea1[1]  ttu_b_mea1[2]: $ttu_b_mea1[2]  ttu_b_mea2: @ttu_b_mea2   ttu_b_mea1: @ttu_b_mea1  \n";
                                        next;
                                }
                                @ttu_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         if (uc($tmd[3]) =~ /TNL/ ) {
                                @tnl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnl_b_mea1[1] =~ /^TR/ ) {
                                        @tnl_b_mea2 = @tnl_b_mea1;
                                        $tnl_b_mea2[3] = $tnl_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tnl_b_mea1 = ();
                                        next;
                                }
                                @tnl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         if (uc($tmd[3]) =~ /TRL/ ) {
                                @trl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trl_b_mea1[1] =~ /^TR/ ) {
                                        @trl_b_mea2 = @trl_b_mea1;
                                        $trl_b_mea2[3] = $trl_b_mea2[3] . '|' . uc($tmd[7]);
                                        @trl_b_mea1 = ();
                                        next;
                                }
                                @trl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }


                        #print "Before if (tmd[3] =~ /TTY/ ). Backend. tmd[3]: $tmd[3]  \n";


                        if (uc($tmd[3]) =~ /TTY/ ) {

                                #print "Inside TTY Backend  \n";

                                @tty_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );

                                #print "Inside TTY Backend. tty_b_mea1[1]: $tty_b_mea1[1]  \n";

                                if ( $tty_b_mea1[1] =~ /^TR/ ) {
                                        @tty_b_mea2 = @tty_b_mea1;
                                        $tty_b_mea2[3] = $tty_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tty_b_mea1 = ();

                                        #print "Inside TTY Backend, if ( $tty_b_mea1[1] =~ /^TR/ ). tty_b_mea1[2]: $tty_b_mea1[2]   tty_b_mea1[3]: $tty_b_mea1[3]   \n";

                                        next;
                                }
                                @tty_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         #Alex Gerlants added Begin -----------------------------------------------------

                         if (uc($tmd[3]) =~ /TTS/ ) {
                                @tts_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tts_b_mea1[1] =~ /^TR/ ) {
                                        @tts_b_mea2 = @tts_b_mea1;
                                        $tts_b_mea2[3] = $tts_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tts_b_mea1 = ();
                                        next;
                                }
                                @tts_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTL/ ) {
                                @ttl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttl_b_mea1[1] =~ /^TR/ ) {
                                        @ttl_b_mea2 = @ttl_b_mea1;
                                        $ttl_b_mea2[3] = $ttl_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ttl_b_mea1 = ();
                                        next;
                                }
                                @ttl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTO/ ) {
                                @tto_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tto_b_mea1[1] =~ /^TR/ ) {
                                        @tto_b_mea2 = @tto_b_mea1;
                                        $tto_b_mea2[3] = $tto_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tto_b_mea1 = ();
                                        next;
                                }
                                @tto_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTT/ ) {
                                @ttt_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttt_b_mea1[1] =~ /^TR/ ) {
                                        @ttt_b_mea2 = @ttt_b_mea1;
                                        $ttt_b_mea2[3] = $ttt_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ttt_b_mea1 = ();
                                        next;
                                }
                                @ttt_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TND/ ) {
                                @tnd_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnd_b_mea1[1] =~ /^TR/ ) {
                                        @tnd_b_mea2 = @tnd_b_mea1;
                                        $tnd_b_mea2[3] = $tnd_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tnd_b_mea1 = ();
                                        next;
                                }
                                @tnd_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TNT/ ) {
                                @tnt_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnt_b_mea1[1] =~ /^TR/ ) {
                                        @tnt_b_mea2 = @tnt_b_mea1;
                                        $tnt_b_mea2[3] = $tnt_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tnt_b_mea1 = ();
                                        next;
                                }
                                @tnt_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TMD/ ) {
                                @tmd_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tmd_b_mea1[1] =~ /^TR/ ) {
                                        @tmd_b_mea2 = @tmd_b_mea1;
                                        $tmd_b_mea2[3] = $tmd_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tmd_b_mea1 = ();
                                        next;
                                }
                                @tmd_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TRD/ ) {
                                @trd_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trd_b_mea1[1] =~ /^TR/ ) {
                                        @trd_b_mea2 = @trd_b_mea1;
                                        $trd_b_mea2[3] = $trd_b_mea2[3] . '|' . uc($tmd[7]);
                                        @trd_b_mea1 = ();
                                        next;
                                }
                                @trd_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         if (uc($tmd[3]) =~ /TRT/ ) {
                                @trt_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trt_b_mea1[1] =~ /^TR/ ) {
                                        @trt_b_mea2 = @trt_b_mea1;
                                        $trt_b_mea2[3] = $trt_b_mea2[3] . '|' . uc($tmd[7]);
                                        @trt_b_mea1 = ();
#print "Inside TRT Backend, if ( $trt_f_mea1[1] =~ /^TR/ ). trt_f_mea1[3]: $trt_f_mea1[3]   trt_f_mea1[3]: $trt_f_mea1[3]   \n";
                                        next;
                                }
                                @trt_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         if (uc($tmd[3]) =~ /TES/ ) {
                                @tes_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tes_b_mea1[1] =~ /^TR/ ) {
                                        @tes_b_mea2 = @tes_b_mea1;
                                        $tes_b_mea2[3] = $tes_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tes_b_mea1 = ();
                                        next;
                                }
                                @tes_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TET/ ) {
                                @tet_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tet_b_mea1[1] =~ /^TR/ ) {
                                        @tet_b_mea2 = @tet_b_mea1;
                                        $tet_b_mea2[3] = $tet_b_mea2[3] . '|' . uc($tmd[7]); 
                                        @tet_b_mea1 = ();
                                        next;
                                }
                                @tet_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /THL/ ) {
                                @thl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $thl_b_mea1[1] =~ /^TR/ ) {
                                        @thl_b_mea2 = @thl_b_mea1;
                                        $thl_b_mea2[3] = $thl_b_mea2[3] . '|' . uc($tmd[7]);
                                        @thl_b_mea1 = ();
                                        next;
                                }
                                @thl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /THD/ ) {
                                @thd_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $thd_b_mea1[1] =~ /^TR/ ) {
                                        @thd_b_mea2 = @thd_b_mea1;
                                        $thd_b_mea2[3] = $thd_b_mea2[3] . '|' . uc($tmd[7]);
                                        @thd_b_mea1 = ();
                                        next;
                                }
                                @thd_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /THT/ ) {
                                @tht_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tht_b_mea1[1] =~ /^TR/ ) {
                                        @tht_b_mea2 = @tht_b_mea1;
                                        $tht_b_mea2[3] = $tht_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tht_b_mea1 = ();
                                        next;
                                }
                                @tht_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /X27/ ) {
                                @x27_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $x27_b_mea1[1] =~ /^TR/ ) {
                                        @x27_b_mea2 = @x27_b_mea1;
                                        $x27_b_mea2[3] = $x27_b_mea2[3] . '|' . uc($tmd[7]);
                                        @x27_b_mea1 = ();
                                        next;
                                }
                                @x27_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /N4T/ ) {
                                @n4t_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $n4t_b_mea1[1] =~ /^TR/ ) {
                                        @n4t_b_mea2 = @n4t_b_mea1;
                                        $n4t_b_mea2[3] = $n4t_b_mea2[3] . '|' . uc($tmd[7]);
                                        @n4t_b_mea1 = ();
                                        next;
                                }
                                @n4t_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /N5T/ ) {
                                @n5t_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $n5t_b_mea1[1] =~ /^TR/ ) {
                                        @n5t_b_mea2 = @n5t_b_mea1;
                                        $n5t_b_mea2[3] = $n5t_b_mea2[3] . '|' . uc($tmd[7]);
                                        @n5t_b_mea1 = ();
                                        next;
                                }
                                @n5t_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }


                         if (uc($tmd[3]) =~ /MDO/ ) {
                                @mdo_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $mdo_b_mea1[1] =~ /^TR/ ) {
                                        @mdo_b_mea2 = @mdo_b_mea1;
                                        $mdo_b_mea2[3] = $mdo_b_mea2[3] . '|' . uc($tmd[7]);
                                        @mdo_b_mea1 = ();
                                        next;
                                }
                                @mdo_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTE/ ) {
                                @tte_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tte_b_mea1[1] =~ /^TR/ ) {
                                        @tte_b_mea2 = @tte_b_mea1;
                                        $tte_b_mea2[3] = $tte_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tte_b_mea1 = ();
                                        next;
                                }
                                @tte_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTZ/ ) {
                                @ttz_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttz_b_mea1[1] =~ /^TR/ ) {
                                        @ttz_b_mea2 = @ttz_b_mea1;
                                        $ttz_b_mea2[3] = $ttz_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ttz_b_mea1 = ();
                                        next;
                                }
                                @ttz_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ARO/ ) {
                                @aro_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $aro_b_mea1[1] =~ /^TR/ ) {
                                        @aro_b_mea2 = @aro_b_mea1;
                                        $aro_b_mea2[3] = $aro_b_mea2[3] . '|' . uc($tmd[7]);
                                        @aro_b_mea1 = ();
                                        next;
                                }
                                @aro_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /BKN/ ) {
                                @bkn_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $bkn_b_mea1[1] =~ /^TR/ ) {
                                        @bkn_b_mea2 = @bkn_b_mea1;
                                        $bkn_b_mea2[3] = $bkn_b_mea2[3] . '|' . uc($tmd[7]);
                                        @bkn_b_mea1 = ();
                                        next;
                                }
                                @bkn_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TPS/ ) {
                                @tps_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tps_b_mea1[1] =~ /^TR/ ) {
                                        @tps_b_mea2 = @tps_b_mea1;
                                        $tps_b_mea2[3] = $tps_b_mea2[3] . '|' . uc($tmd[7]);
                                        @tps_b_mea1 = ();
                                        next;
                                }
                                @tps_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ISU/ ) {
                                @isu_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $isu_b_mea1[1] =~ /^TR/ ) {
                                        @isu_b_mea2 = @isu_b_mea1;
                                        $isu_b_mea2[3] = $isu_b_mea2[3] . '|' . uc($tmd[7]);
                                        @isu_b_mea1 = ();
                                        next;
                                }
                                @isu_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITL/ ) {
                                @itl_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itl_b_mea1[1] =~ /^TR/ ) {
                                        @itl_b_mea2 = @itl_b_mea1;
                                        $itl_b_mea2[3] = $itl_b_mea2[3] . '|' . uc($tmd[7]);
                                        @itl_b_mea1 = ();
                                        next;
                                }
                                @itl_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITD/ ) {
                                @itd_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itd_b_mea1[1] =~ /^TR/ ) {
                                        @itd_b_mea2 = @itd_b_mea1;
                                        $itd_b_mea2[3] = $itd_b_mea2[3] . '|' . uc($tmd[7]);
                                        @itd_b_mea1 = ();
                                        next;
                                }
                                @itd_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITT/ ) {
                                @itt_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itt_b_mea1[1] =~ /^TR/ ) {
                                        @itt_b_mea2 = @itt_b_mea1;
                                        $itt_b_mea2[3] = $itt_b_mea2[3] . '|' . uc($tmd[7]);
                                        @itt_b_mea1 = ();
                                        next;
                                }
                                @itt_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /UPT/ ) {
                                @upt_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $upt_b_mea1[1] =~ /^TR/ ) {
                                        @upt_b_mea2 = @upt_b_mea1;
                                        $upt_b_mea2[3] = $upt_b_mea2[3] . '|' . uc($tmd[7]);
                                        @upt_b_mea1 = ();
                                        next;
                                }
                                @upt_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                         if (uc($tmd[3]) =~ /ULT/ ) {
                                @ult_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ult_b_mea1[1] =~ /^TR/ ) {
                                        @ult_b_mea2 = @ult_b_mea1;
                                        $ult_b_mea2[3] = $ult_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ult_b_mea1 = ();
                                        next;
                                }
                                @ult_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                         if (uc($tmd[3]) =~ /YPN/ ) {
                                @ypn_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ypn_b_mea1[1] =~ /^TR/ ) {
                                        @ypn_b_mea2 = @ypn_b_mea1;
                                        $ypn_b_mea2[3] = $ypn_b_mea2[3] . '|' . uc($tmd[7]);
                                        @ypn_b_mea1 = ();
                                        next;
                                }
                                @ypn_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                        if (uc($tmd[3]) =~ /DPA/ ) {
                                @dpa_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $dpa_b_mea1[1] =~ /^TR/ ) {
                                        @dpa_b_mea2 = @dpa_b_mea1;
                                        $dpa_b_mea2[3] = $dpa_b_mea2[3] . '|' . uc($tmd[7]);
                                        @dpa_b_mea1 = ();
                                        next;
                                }
                                @dpa_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                        if (uc($tmd[3]) =~ /YSR/ ) {
                                @ysr_b_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ysr_b_mea1[1] =~ /^TR/ ) {
                                        @ysr_b_mea2 = @ysr_b_mea1;
                                        $ysr_b_mea2[3] = $ysr_b_mea2[3] . '|' . uc($tmd[7]); 
                                        @ysr_b_mea1 = ();
                                        next;
                                }
                                @ysr_b_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }


                         #Alex Gerlants added End ------------------------------------------------------


                }  # end of backend
=cut

#print ("Before Starting  Frontend. psd1[7]: $psd1[7]   \n");


# Starting  Frontend

                if ($psd1[7] =~ /^11/ ) {

                        #print "Inside psd1[7] =~ /^11/  Frontend  cid_item[$item_num][1]: $cid_item[$item_num][1] cid_item[$item_num][2]: $cid_item[$item_num][2]  \n";

                        @tmd = split( /$elemDelm/, $cid_item[$item_num][1] );
                        if (uc($tmd[3]) =~ /TEL/ ) {
                                @tel_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tel_f_mea1[1] =~ /^TR/ ) {
                                        @tel_f_mea2 = @tel_f_mea1;
                                        $tel_f_mea2[3] = $tel_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tel_f_mea1 = ();
                                        next;
                                }
                                @tel_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /ITH/ ) {
                                @ith_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ith_f_mea1[1] =~ /^TR/ ) {
                                        @ith_f_mea2 = @ith_f_mea1;
                                        $ith_f_mea2[3] = $ith_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ith_f_mea1 = ();
                                        next;
                                }
                                @ith_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TTU/ ) {

                                @ttu_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttu_f_mea1[1] =~ /^TR/ ) {
                                        @ttu_f_mea2 = @ttu_f_mea1;
                                        $ttu_f_mea2[3] = $ttu_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ttu_f_mea1 = ();
                                        next;
                                }
                                @ttu_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

               #print "Before if (tmd[3] =~ /TTY/ ). Frontend. tmd[3]: $tmd[3]  \n";


                        if (uc($tmd[3]) =~ /TTY/ ) {
                                @tty_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );

                                #print "Inside TTY Frontend. tty_f_mea1[1]: $tty_f_mea1[1]  \n";

                                if ( $tty_f_mea1[1] =~ /^TR/ ) {
                                        @tty_f_mea2 = @tty_f_mea1;
                                        $tty_f_mea2[3] = $tty_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tty_f_mea1 = ();

                                        #print "Inside TTY Frontend, if ( $tty_f_mea1[1] =~ /^TR/ ). tty_f_mea1[3]: $tty_f_mea1[3]   tty_f_mea1[3]: $tty_f_mea1[3]   \n";

                                        next;
                                }
                                @ttu_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TNL/ ) {
                                @tnl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnl_f_mea1[1] =~ /^TR/ ) {
                                        @tnl_f_mea2 = @tnl_f_mea1;
                                        $tnl_f_mea2[3] = $tnl_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tnl_f_mea1 = ();
                                        next;
                                }
                                @tnl_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TRL/ ) {
                                @trl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trl_f_mea1[1] =~ /^TR/ ) {
                                        @trl_f_mea2 = @trl_f_mea1;
                                        $trl_f_mea2[3] = $trl_f_mea2[3] . '|' . uc($tmd[7]);
                                        @trl_f_mea1 = ();
                                        next;
                                }
                                @trl_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TET/ ) {
                                @tet_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tet_f_mea1[1] =~ /^TR/ ) {
                                        @tet_f_mea2 = @tet_f_mea1;
                                        $tet_f_mea2[3] = $tet_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tet_f_mea1 = ();
                                        next;
                                }
                                @tet_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TTL/ ) {
                                @ttl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttl_f_mea1[1] =~ /^TR/ ) {
                                        @ttl_f_mea2 = @ttl_f_mea1;
                                        $ttl_f_mea2[3] = $ttl_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ttl_f_mea1 = ();
                                        next;
                                }
                                @ttl_l_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TTT/ ) {
                                @ttt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttt_f_mea1[1] =~ /^TR/ ) {
                                        @ttt_f_mea2 = @ttt_f_mea1;
                                        $ttt_f_mea2[3] = $ttt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ttt_f_mea1 = ();
                                        next;
                                }
                                @ttt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TNT/ ) {
                                @tnt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnt_f_mea1[1] =~ /^TR/ ) {
                                        @tnt_f_mea2 = @tnt_f_mea1;
                                        $tnt_f_mea2[3] = $tnt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tnt_f_mea1 = ();
                                        next;
                                }
                                @tnt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TRT/ ) {
                                @trt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trt_f_mea1[1] =~ /^TR/ ) {
                                        @trt_f_mea2 = @trt_f_mea1;
                                        $trt_f_mea2[3] = $trt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @trt_f_mea1 = ();
                                        next;
                                }
                                @trt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TES/ ) {
                                @tes_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tes_f_mea1[1] =~ /^TR/ ) {
                                        @tes_f_mea2 = @tes_f_mea1;
                                        $tes_f_mea2[3] = $tes_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tes_f_mea1 = ();
                                        next;
                                }
                                @tes_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TTS/ ) {
                                @tts_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tts_f_mea1[1] =~ /^TR/ ) {
                                        @tts_f_mea2 = @tts_f_mea1;
                                        $tts_f_mea2[3] = $tts_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tts_f_mea1 = ();
                                        next;
                                }
                                @tts_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TTO/ ) {
                                @tto_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tto_f_mea1[1] =~ /^TR/ ) {
                                        @tto_f_mea2 = @tto_f_mea1;
                                        $tto_f_mea2[3] = $tto_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tto_f_mea1 = ();
                                        next;
                                }
                                @tto_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TND/ ) {
                                @tnd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tnd_f_mea1[1] =~ /^TR/ ) {
                                        @tnd_f_mea2 = @tnd_f_mea1;
                                        $tnd_f_mea2[3] = $tnd_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tnd_f_mea1 = ();
                                        next;
                                }
                                @tnd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /TRD/ ) {
                                @trd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $trd_f_mea1[1] =~ /^TR/ ) {
                                        @trd_f_mea2 = @trd_f_mea1;
                                        $trd_f_mea2[3] = $trd_f_mea2[3] . '|' . uc($tmd[7]);
                                        @trd_f_mea1 = ();
                                        next;
                                }
                                @trd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        #Alex Gerlants added Begin -----------------------------------------------------

                        if (uc($tmd[3]) =~ /TMD/ ) {
                                @tmd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tmd_f_mea1[1] =~ /^TR/ ) {
                                        @tmd_f_mea2 = @tmd_f_mea1;
                                        $tmd_f_mea2[3] = $tmd_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tmd_f_mea1 = ();
                                        next;
                                }
                                @tmd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /THL/ ) {
                                @thl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $thl_f_mea1[1] =~ /^TR/ ) {
                                        @thl_f_mea2 = @thl_f_mea1;
                                        $thl_f_mea2[3] = $thl_f_mea2[3] . '|' . uc($tmd[7]);
                                        @thl_f_mea1 = ();
                                        next;
                                }
                                @thl_l_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /THD/ ) {
                                @thd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $thd_f_mea1[1] =~ /^TR/ ) {
                                        @thd_f_mea2 = @thd_f_mea1;
                                        $thd_f_mea2[3] = $thd_f_mea2[3] . '|' . uc($tmd[7]);
                                        @thd_f_mea1 = ();
                                        next;
                                }
                                @thd_l_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /THT/ ) {
                                @tht_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tht_f_mea1[1] =~ /^TR/ ) {
                                        @tht_f_mea2 = @tht_f_mea1;
                                        $tht_f_mea2[3] = $tht_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tht_f_mea1 = ();
                                        next;
                                }
                                @tht_l_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                        }

                        if (uc($tmd[3]) =~ /X27/ ) {
                               @x27_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                               if ( $x27_f_mea1[1] =~ /^TR/ ) {
                                       @x27_f_mea2 = @x27_f_mea1;
                                       $x27_f_mea2[3] = $x27_f_mea2[3] . '|' . uc($tmd[7]);
                                       @x27_f_mea1 = ();
                                       next;
                               }
                               @x27_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                               next;
                        }

                         if (uc($tmd[3]) =~ /N4T/ ) {
                                @n4t_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $n4t_f_mea1[1] =~ /^TR/ ) {
                                        @n4t_f_mea2 = @n4t_f_mea1;
                                        $n4t_f_mea2[3] = $n4t_f_mea2[3] . '|' . uc($tmd[7]);
                                        @n4t_f_mea1 = ();
                                        next;
                                }
                                @n4t_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /N5T/ ) {
                                @n5t_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $n5t_f_mea1[1] =~ /^TR/ ) {
                                        @n5t_f_mea2 = @n5t_f_mea1;
                                        $n5t_f_mea2[3] = $n5t_f_mea2[3] . '|' . uc($tmd[7]);
                                        @n5t_f_mea1 = ();
                                        next;
                                }
                                @n5t_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

=pod
                         if (uc($tmd[3]) =~ /ZLB/ ) {
                                @zlb_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $zlb_f_mea1[1] =~ /^TR/ ) {
                                        @zlb_f_mea2 = @zlb_f_mea1;
                                        $zlb_f_mea2[3] = $zlb_f_mea2[3] . '|' . uc($tmd[7]);
                                        @zlb_f_mea1 = ();
                                        next;
                                }
                                @zlb_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ZLT/ ) {
                                @zlt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $zlt_f_mea1[1] =~ /^TR/ ) {
                                        @zlt_f_mea2 = @zlt_f_mea1;
                                        $zlt_f_mea2[3] = $zlt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @zlt_f_mea1 = ();
                                        next;
                                }
                                @zlt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }
=cut

                         if (uc($tmd[3]) =~ /MDO/ ) {
                                @mdo_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $mdo_f_mea1[1] =~ /^TR/ ) {
                                        @mdo_f_mea2 = @mdo_f_mea1;
                                        $mdo_f_mea2[3] = $mdo_f_mea2[3] . '|' . uc($tmd[7]);
                                        @mdo_f_mea1 = ();
                                        next;
                                }
                                @mdo_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTE/ ) {
                                @tte_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tte_f_mea1[1] =~ /^TR/ ) {
                                        @tte_f_mea2 = @tte_f_mea1;
                                        $tte_f_mea2[3] = $tte_f_mea2[3] . '|' . uc($tmd[7]);
                                        @tte_f_mea1 = ();
                                        next;
                                }
                                @tte_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TTZ/ ) {
                                @ttz_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ttz_f_mea1[1] =~ /^TR/ ) {
                                        @ttz_f_mea2 = @ttz_f_mea1;
                                        $ttz_f_mea2[3] = $ttz_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ttz_f_mea1 = ();
                                        next;
                                }
                                @ttz_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ARO/ ) {
                                @aro_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $aro_f_mea1[1] =~ /^TR/ ) {
                                        @aro_f_mea2 = @aro_f_mea1;
                                        $aro_f_mea2[3] = $aro_f_mea2[3] . '|' . uc($tmd[7]);
                                        @aro_f_mea1 = ();
                                        next;
                                }
                                @aro_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /BKN/ ) {
                                @bkn_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $bkn_f_mea1[1] =~ /^TR/ ) {
                                        @bkn_f_mea2 = @bkn_f_mea1;
                                        $bkn_f_mea2[3] = $bkn_f_mea2[3] . '|' . uc($tmd[7]);
                                        @bkn_f_mea1 = ();
                                        next;
                                }
                                @bkn_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /TPS/ ) {
                                @tps_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $tps_f_mea1[1] =~ /^TR/ ) {
                                        @tps_f_mea2 = @tps_f_mea1;
                                        $tps_f_mea2[3] = $tps_f_mea2[3] . '|' . uc($tmd[7]); 
                                        @tps_f_mea1 = ();
                                        next;
                                }
                                @tps_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ISU/ ) {
                                @isu_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $isu_f_mea1[1] =~ /^TR/ ) {
                                        @isu_f_mea2 = @isu_f_mea1;
                                        $isu_f_mea2[3] = $isu_f_mea2[3] . '|' . uc($tmd[7]);
                                        @isu_f_mea1 = ();
                                        next;
                                }
                                @isu_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITL/ ) {
                                @itl_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itl_f_mea1[1] =~ /^TR/ ) {
                                        @itl_f_mea2 = @itl_f_mea1;
                                        $itl_f_mea2[3] = $itl_f_mea2[3] . '|' . uc($tmd[7]);
                                        @itl_f_mea1 = ();
                                        next;
                                }
                                @itl_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITD/ ) {
                                @itd_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itd_f_mea1[1] =~ /^TR/ ) {
                                        @itd_f_mea2 = @itd_f_mea1;
                                        $itd_f_mea2[3] = $itd_f_mea2[3] . '|' . uc($tmd[7]);
                                        @itd_f_mea1 = ();
                                        next;
                                }
                                @itd_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /ITT/ ) {
                                @itt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $itt_f_mea1[1] =~ /^TR/ ) {
                                        @itt_f_mea2 = @itt_f_mea1;
                                        $itt_f_mea2[3] = $itt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @itt_f_mea1 = ();
                                        next;
                                }
                                @itt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                         }

                         if (uc($tmd[3]) =~ /UPT/ ) {
                                @upt_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $upt_f_mea1[1] =~ /^TR/ ) {
                                        @upt_f_mea2 = @upt_f_mea1;
                                        $upt_f_mea2[3] = $upt_f_mea2[3] . '|' . uc($tmd[7]);
                                        @upt_f_mea1 = ();
                                        next;
                                }
                                @upt_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                         if (uc($tmd[3]) =~ /ULT/ ) {

             #print "Inside if (uc($tmd[3]) =~ /ULT/ ).  uc(tmd[3]): uc($tmd[3]) \n";

                                @ult_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ult_f_mea1[1] =~ /^TR/ ) {
                                        @ult_f_mea2 = @ult_f_mea1;
                                        @ult_f_mea1 = ();

              #print "Before $ult_f_mea2 = ...  ult_f_mea2: $ult_f_mea2 \n";

                                        $ult_f_mea2[3] = $ult_f_mea2[3] . '|' . uc($tmd[7]);

              #print "After  $ult_f_mea2 = ...  ult_f_mea2: $ult_f_mea2 \n";

                                        next;
                                }
                                @ult_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                         if (uc($tmd[3]) =~ /YPN/ ) {
                                @ypn_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ypn_f_mea1[1] =~ /^TR/ ) {
                                        @ypn_f_mea2 = @ypn_f_mea1;
                                        $ypn_f_mea2[3] = $ypn_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ypn_f_mea1 = ();
                                        next;
                                }
                                @ypn_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;
                         }

                        if (uc($tmd[3]) =~ /DPA/ ) {
                                @dpa_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $dpa_f_mea1[1] =~ /^TR/ ) {
                                        @dpa_f_mea2 = @dpa_f_mea1;
                                        $dpa_f_mea2[3] = $dpa_f_mea2[3] . '|' . uc($tmd[7]);
                                        @dpa_f_mea1 = ();
                                        next;
                                }
                                @dpa_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                        if (uc($tmd[3]) =~ /YSR/ ) {
                                @ysr_f_mea1 = split ( /$elemDelm/, $cid_item[$item_num][2] );
                                if ( $ysr_f_mea1[1] =~ /^TR/ ) {
                                        @ysr_f_mea2 = @ysr_f_mea1;
                                        $ysr_f_mea2[3] = $ysr_f_mea2[3] . '|' . uc($tmd[7]);
                                        @ysr_f_mea1 = ();
                                        next;
                                }
                                @ysr_f_mea2 = split ( /$elemDelm/, $cid_item[$item_num][3] );
                                next;

                        }

                         #Alex Gerlants added End -------------------------------------------------------


                }  # end of front end



 }

#print ("before Starting handle CID**68\n");
#print ("shipment[$i]: $shipment[$i]\n");

=for comment

        # Chemistry   Starting handle CID**68
        while (uc($shipment[$i]) !~ /CID\*\*68/) {
                $i ++;
        }
        $i ++;

        #print ("shipment[$i]: $shipment[$i]\n");

        while  (uc($shipment[$i]) !~ /SE/ ) {
                @mea = split (/$elemDelm/, $shipment[$i ++]);

                #print ("i: $i   shipment[$i]: $$shipment[$i]    mea[2]: $mea[2]\n");

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

=cut

#print ("zmg: $zmg\n");

#Update data_in_863 table

#print ("Before INSERT INTO data_in_863. file_id: $file_id  Coil: $lin[9]   \n");
#print "zfe: $zfe   zcu: $zcu   zmn: $zmn   zmg: $zmg   zcr: $zcr   zni: $zni   zzn: $zzn   zti: $zti   zgh: $zgh   zal: $zal    \n";

 $dbh->do(" INSERT INTO dbo.data_in_863 (    EDI_FILE_ID,
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
                                                V,
                                                WT,
                                                TTS_B_M1,
                                                TTS_B_M2,
                                                TTL_B_M1,
                                                TTL_B_M2,
                                                TTO_B_M1,
                                                TTO_B_M2,
                                                TTT_B_M1,
                                                TTT_B_M2,
                                                TND_B_M1,
                                                TND_B_M2,
                                                TNT_B_M1,
                                                TNT_B_M2,
                                                TMD_B_M1,
                                                TMD_B_M2,
                                                TMD_F_M1,
                                                TMD_F_M2,
                                                TRD_B_M1,
                                                TRD_B_M2,
                                                TRT_B_M1,
                                                TRT_B_M2,
                                                TES_B_M1,
                                                TES_B_M2,
                                                TET_B_M1,
                                                TET_B_M2,
                                                THL_B_M1,
                                                THL_B_M2,
                                                THL_F_M1,
                                                THL_F_M2,
                                                THD_B_M1,
                                                THD_B_M2,
                                                THD_F_M1,
                                                THD_F_M2,
                                                THT_B_M1,
                                                THT_B_M2,
                                                THT_F_M1,
                                                THT_F_M2,
                                                PROC_DATE,
                                                PROC_TIME,
                                                PROD_DATE,
                                                PROD_TIME,
                                                CASH_DATE,
                                                LUBE_DATE,
                                                LUBE_WEIGHT,
                                                LUBE_WEIGHT_UOM,
                                                SHIP_DATE,
                                                SHIP_TIME,
                                                B,
                                                CA,
                                                CD,
                                                LB,
                                                SX,
                                                PB,
                                                SB,
                                                SN,
                                                ZR,
                                                X27_B_M1,
                                                X27_B_M2,
                                                X27_F_M1,
                                                X27_F_M2,
                                                N4T_B_M1,
                                                N4T_B_M2,
                                                N4T_F_M1,
                                                N4T_F_M2,
                                                N5T_B_M1,
                                                N5T_B_M2,
                                                N5T_F_M1,
                                                N5T_F_M2,

                                                ZLT,
                                                ZLB,
                                                ZLN,
                                                ZLX,

                                                MDO_B_M1,
                                                MDO_B_M2,
                                                MDO_F_M1,
                                                MDO_F_M2,
                                                TTE_B_M1,
                                                TTE_B_M2,
                                                TTE_F_M1,
                                                TTE_F_M2,
                                                TTZ_B_M1,
                                                TTZ_B_M2,
                                                TTZ_F_M1,
                                                TTZ_F_M2,
                                                ARO_B_M1,
                                                ARO_B_M2,
                                                ARO_F_M1,
                                                ARO_F_M2,
                                                BKN_B_M1,
                                                BKN_B_M2,
                                                BKN_F_M1,
                                                BKN_F_M2,
                                                TPS_B_M1,
                                                TPS_B_M2,
                                                TPS_F_M1,
                                                TPS_F_M2,
                                                ISU_B_M1,
                                                ISU_B_M2,
                                                ISU_F_M1,
                                                ISU_F_M2,
                                                ITL_B_M1,
                                                ITL_B_M2,
                                                ITL_F_M1,
                                                ITL_F_M2,
                                                ITD_B_M1,
                                                ITD_B_M2,
                                                ITD_F_M1,
                                                ITD_F_M2,
                                                ITT_B_M1,
                                                ITT_B_M2,
                                                ITT_F_M1,
                                                ITT_F_M2,
                                                UPT_B_M1,
                                                UPT_B_M2,
                                                UPT_F_M1,
                                                UPT_F_M2,
                                                ULT_B_M1,
                                                ULT_B_M2,
                                                ULT_F_M1,
                                                ULT_F_M2,
                                                YPN_B_M1,
                                                YPN_B_M2,
                                                YPN_F_M1,
                                                YPN_F_M2,
                                                ITH_B_M1,
                                                ITH_B_M2,
                                                ITH_F_M1,
                                                ITH_F_M2,
                                                DPA_B_M1,
                                                DPA_B_M2,
                                                DPA_F_M1,
                                                DPA_F_M2,
                                                YSR_B_M1,
                                                YSR_B_M2,
                                                YSR_F_M1,
                                                YSR_F_M2,
                                                ZAS,
                                                BI,
                                                ZB,
                                                ZV,
                                                CO,
                                                IK,
                                                ZP,
                                                SS,
                                                SH,
                                                EDI_FILE_NAME,
                                                CHEMICAL_TEST_DATE,
                                                GRADE,
                                                tty_b_uom,
                                                ttu_b_uom,
                                                tel_b_uom,
                                                trl_b_uom,
                                                tnl_b_uom,
                                                ith_b_uom,
                                                tts_b_uom,
                                                ttl_b_uom,
                                                tto_b_uom,
                                                ttt_b_uom,
                                                tnd_b_uom,
                                                tnt_b_uom,
                                                tmd_b_uom,
                                                trd_b_uom,
                                                trt_b_uom,
                                                tes_b_uom,
                                                tet_b_uom,
                                                thl_b_uom,
                                                thd_b_uom,
                                                tht_b_uom,
                                                x27_b_uom,
                                                n4t_b_uom,
                                                n5t_b_uom,
                                                mdo_b_uom,
                                                tte_b_uom,
                                                ttz_b_uom,
                                                aro_b_uom,
                                                bkn_b_uom,
                                                tps_b_uom,
                                                isu_b_uom,
                                                itl_b_uom,
                                                itd_b_uom,
                                                itt_b_uom,
                                                upt_b_uom,
                                                ult_b_uom,
                                                ypn_b_uom,
                                                dpa_b_uom,
                                                ysr_b_uom,
                                                tty_f_uom,
                                                ttu_f_uom,
                                                tel_f_uom,
                                                trl_f_uom,
                                                tnl_f_uom,
                                                ith_f_uom,
                                                tts_f_uom,
                                                ttl_f_uom,
                                                tto_f_uom,
                                                ttt_f_uom,
                                                tnd_f_uom,
                                                tnt_f_uom,
                                                tmd_f_uom,
                                                trd_f_uom,
                                                trt_f_uom,
                                                tes_f_uom,
                                                tet_f_uom,
                                                thl_f_uom,
                                                thd_f_uom,
                                                tht_f_uom,
                                                x27_f_uom,
                                                n4t_f_uom,
                                                n5t_f_uom,
                                                mdo_f_uom,
                                                tte_f_uom,
                                                ttz_f_uom,
                                                aro_f_uom,
                                                bkn_f_uom,
                                                tps_f_uom,
                                                isu_f_uom,
                                                itl_f_uom,
                                                itd_f_uom,
                                                itt_f_uom,
                                                upt_f_uom,
                                                ult_f_uom,
                                                ypn_f_uom,
                                                dpa_f_uom,
                                                ysr_f_uom,
                                                date_time_received
                                                )
                                         VALUES ( $file_id,
                                                '$coil_num',
                                                '$width',
                                                '$thickness',
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
                                                '$v',
                                                '$net_weight',
                                                '$tts_b_mea1[3]',
                                                '$tts_b_mea2[3]',
                                                '$ttl_b_mea1[3]',
                                                '$ttl_b_mea2[3]',
                                                '$tto_b_mea1[3]',
                                                '$tto_b_mea2[3]',
                                                '$ttt_b_mea1[3]',
                                                '$ttt_b_mea2[3]',
                                                '$tnd_b_mea1[3]',
                                                '$tnd_b_mea2[3]',
                                                '$tnt_b_mea1[3]',
                                                '$tnt_b_mea2[3]',
                                                '$tmd_b_mea1[3]',
                                                '$tmd_b_mea2[3]',
                                                '$tmd_f_mea1[3]',
                                                '$tmd_f_mea2[3]',
                                                '$trd_b_mea1[3]',
                                                '$trd_b_mea2[3]',
                                                '$trt_b_mea1[3]',
                                                '$trt_b_mea2[3]',
                                                '$tes_b_mea1[3]',
                                                '$tes_b_mea2[3]',
                                                '$tet_b_mea1[3]',
                                                '$tet_b_mea2[3]',
                                                '$thl_b_mea1[3]',
                                                '$thl_b_mea2[3]',
                                                '$thl_f_mea1[3]',
                                                '$thl_f_mea2[3]',
                                                '$thd_b_mea1[3]',
                                                '$thd_b_mea2[3]',
                                                '$thd_f_mea1[3]',
                                                '$thd_f_mea2[3]',
                                                '$tht_b_mea1[3]',
                                                '$tht_b_mea2[3]',
                                                '$tht_f_mea1[3]',
                                                '$tht_f_mea2[3]',
                                                '$proc_date',
                                                '$proc_time',
                                                '$prod_date',
                                                '$prod_time',
                                                '$cash_date',
                                                '$lube_date',
                                                '$lube_weight',
                                                '$lube_weight_uom',
                                                '$ship_date',
                                                '$ship_time',
                                                '$zb',
                                                '$zca',
                                                '$zcd',
                                                '$lb',
                                                '$sx',
                                                '$zpb',
                                                '$zsb',
                                                '$zsn',
                                                '$zzr',
                                                '$x27_b_mea1[3]',
                                                '$x27_b_mea2[3]',
                                                '$x27_f_mea1[3]',
                                                '$x27_f_mea2[3]',
                                                '$n4t_b_mea1[3]',
                                                '$n4t_b_mea2[3]',
                                                '$n4t_f_mea1[3]',
                                                '$n4t_f_mea2[3]',
                                                '$n5t_b_mea1[3]',
                                                '$n5t_b_mea2[3]',
                                                '$n5t_f_mea1[3]',
                                                '$n5t_f_mea2[3]',

                                                '$zlt',
                                                '$zlb', 
                                                '$zln',
                                                '$zlx',

                                                '$mdo_b_mea1[3]',
                                                '$mdo_b_mea2[3]',
                                                '$mdo_f_mea1[3]',
                                                '$mdo_f_mea2[3]',
                                                '$tte_b_mea1[3]',
                                                '$tte_b_mea2[3]',
                                                '$tte_f_mea1[3]',
                                                '$tte_f_mea2[3]',
                                                '$ttz_b_mea1[3]',
                                                '$ttz_b_mea2[3]',
                                                '$ttz_f_mea1[3]',
                                                '$ttz_f_mea2[3]',
                                                '$aro_b_mea1[3]',
                                                '$aro_b_mea2[3]',
                                                '$aro_f_mea1[3]',
                                                '$aro_f_mea2[3]',
                                                '$bkn_b_mea1[3]',
                                                '$bkn_b_mea2[3]',
                                                '$bkn_f_mea1[3]',
                                                '$bkn_f_mea2[3]',
                                                '$tps_b_mea1[3]',
                                                '$tps_b_mea2[3]',
                                                '$tps_f_mea1[3]',
                                                '$tps_f_mea2[3]',
                                                '$isu_b_mea1[3]',
                                                '$isu_b_mea2[3]',
                                                '$isu_f_mea1[3]',
                                                '$isu_f_mea2[3]',
                                                '$itl_b_mea1[3]',
                                                '$itl_b_mea2[3]',
                                                '$itl_f_mea1[3]',
                                                '$itl_f_mea2[3]',
                                                '$itd_b_mea1[3]',
                                                '$itd_b_mea2[3]',
                                                '$itd_f_mea1[3]',
                                                '$itd_f_mea2[3]',
                                                '$itt_b_mea1[3]',
                                                '$itt_b_mea2[3]',
                                                '$itt_f_mea1[3]',
                                                '$itt_f_mea2[3]',
                                                '$upt_b_mea1[3]',
                                                '$upt_b_mea2[3]',
                                                '$upt_f_mea1[3]',
                                                '$upt_f_mea2[3]',
                                                '$ult_b_mea1[3]',
                                                '$ult_b_mea2[3]',
                                                '$ult_f_mea1[3]',
                                                '$ult_f_mea2[3]',
                                                '$ypn_b_mea1[3]',
                                                '$ypn_b_mea2[3]',
                                                '$ypn_f_mea1[3]',
                                                '$ypn_f_mea2[3]',
                                                '$ith_b_mea1[3]',
                                                '$ith_b_mea2[3]',
                                                '$ith_f_mea1[3]',
                                                '$ith_f_mea2[3]',
                                                '$dpa_b_mea1[3]',
                                                '$dpa_b_mea2[3]',
                                                '$dpa_f_mea1[3]',
                                                '$dpa_f_mea2[3]',
                                                '$ysr_b_mea1[3]',
                                                '$ysr_b_mea2[3]',
                                                '$ysr_f_mea1[3]',
                                                '$ysr_f_mea2[3]',
                                                '$zas',
                                                '$zbi',
                                                '$zb',
                                                '$zv',
                                                '$zco',
                                                '$ik',
                                                '$zp',
                                                '$ss',
                                                '$sh',
                                                '$edi_file',
                                                '$chemical_test_date',
                                                '$grade',
                                                '$tty_b_mea2[4]',
                                                '$ttu_b_mea2[4]',
                                                '$tel_b_mea2[4]',
                                                '$trl_b_mea2[4]',
                                                '$tnl_b_mea2[4]',
                                                '$ith_b_mea2[4]',
                                                '$tts_b_mea2[4]',
                                                '$ttl_b_mea2[4]',
                                                '$tto_b_mea2[4]',
                                                '$ttt_b_mea2[4]',
                                                '$tnd_b_mea2[4]',
                                                '$tnt_b_mea2[4]',
                                                '$tmd_b_mea2[4]',
                                                '$trd_b_mea2[4]',
                                                '$trt_b_mea2[4]',
                                                '$tes_b_mea2[4]',
                                                '$tet_b_mea2[4]',
                                                '$thl_b_mea2[4]',
                                                '$thd_b_mea2[4]',
                                                '$tht_b_mea2[4]',
                                                '$x27_b_mea2[4]',
                                                '$n4t_b_mea2[4]',
                                                '$n5t_b_mea2[4]',
                                                '$mdo_b_mea2[4]',
                                                '$tte_b_mea2[4]',
                                                '$ttz_b_mea2[4]',
                                                '$aro_b_mea2[4]',
                                                '$bkn_b_mea2[4]',
                                                '$tps_b_mea2[4]',
                                                '$isu_b_mea2[4]',
                                                '$itl_b_mea2[4]',
                                                '$itd_b_mea2[4]',
                                                '$itt_b_mea2[4]',
                                                '$upt_b_mea2[4]',
                                                '$ult_b_mea2[4]',
                                                '$ypn_b_mea2[4]',
                                                '$dpa_b_mea2[4]',
                                                '$ysr_b_mea2[4]',
                                                '$tty_f_mea2[4]',
                                                '$ttu_f_mea2[4]',
                                                '$tel_f_mea2[4]',
                                                '$trl_f_mea2[4]',
                                                '$tnl_f_mea2[4]',
                                                '$ith_f_mea2[4]',
                                                '$tts_f_mea2[4]',
                                                '$ttl_f_mea2[4]',
                                                '$tto_f_mea2[4]',
                                                '$ttt_f_mea2[4]',
                                                '$tnd_f_mea2[4]',
                                                '$tnt_f_mea2[4]',
                                                '$tmd_f_mea2[4]',
                                                '$trd_f_mea2[4]',
                                                '$trt_f_mea2[4]',
                                                '$tes_f_mea2[4]',
                                                '$tet_f_mea2[4]',
                                                '$thl_f_mea2[4]',
                                                '$thd_f_mea2[4]',
                                                '$tht_f_mea2[4]',
                                                '$x27_f_mea2[4]',
                                                '$n4t_f_mea2[4]',
                                                '$n5t_f_mea2[4]',
                                                '$mdo_f_mea2[4]',
                                                '$tte_f_mea2[4]',
                                                '$ttz_f_mea2[4]',
                                                '$aro_f_mea2[4]',
                                                '$bkn_f_mea2[4]',
                                                '$tps_f_mea2[4]',
                                                '$isu_f_mea2[4]',
                                                '$itl_f_mea2[4]',
                                                '$itd_f_mea2[4]',
                                                '$itt_f_mea2[4]',
                                                '$upt_f_mea2[4]',
                                                '$ult_f_mea2[4]',
                                                '$ypn_f_mea2[4]',
                                                '$dpa_f_mea2[4]',
                                                '$ysr_f_mea2[4]',
                                                '$dt'
                                                )
                                        ");

#print ("Before commit and after INSERT INTO data_in_863 \n");

$dbh->do("commit");

#print ("After commit\n");


my $coil_org_num     = $lin[13];
$coil_org2 = $coil_org_num;


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

#Alex Gerlants added Begin -----------------------------------------------------

$tts_b_mea1[3] = ();
$tts_b_mea2[3] = ();

$ttl_b_mea1[3] = ();
$ttl_b_mea2[3] = ();

$tto_b_mea1[3] = ();
$tto_b_mea2[3] = ();

$ttt_b_mea1[3] = ();
$ttt_b_mea2[3] = ();

$tnd_b_mea1[3] = ();
$tnd_b_mea2[3] = ();

$tnt_b_mea1[3] = ();
$tnt_b_mea2[3] = ();

$tmd_b_mea1[3] = ();
$tmd_b_mea2[3] = ();

$tmd_f_mea1[3] = ();
$tmd_f_mea2[3] = ();

$trd_b_mea1[3] = ();
$trd_b_mea2[3] = ();

$trt_b_mea1[3] = ();
$trt_b_mea2[3] = ();

$tes_b_mea1[3] = ();
$tes_b_mea2[3] = ();

$tet_b_mea1[3] = ();
$tet_b_mea2[3] = ();

$thl_b_mea1[3] = ();
$thl_b_mea2[3] = ();
$thl_f_mea1[3] = ();
$thl_f_mea2[3] = ();

$thd_b_mea1[3] = ();
$thd_b_mea2[3] = ();
$thd_f_mea1[3] = ();
$thd_f_mea2[3] = ();

$tht_b_mea1[3] = ();
$tht_b_mea2[3] = ();
$tht_f_mea1[3] = ();
$tht_f_mea2[3] = ();

$x27_b_mea1[3] = ();
$x27_b_mea2[3] = ();
$x27_f_mea1[3] = ();
$x27_f_mea2[3] = ();

$n4t_b_mea1[3] = ();
$n4t_b_mea2[3] = ();
$n4t_f_mea1[3] = ();
$n4t_f_mea2[3] = ();

$n5t_b_mea1[3] = ();
$n5t_b_mea2[3] = ();
$n5t_f_mea1[3] = ();
$n5t_f_mea2[3] = ();

=pod
$zlb_b_mea1[3] = ();
$zlb_b_mea2[3] = ();
$zlb_f_mea1[3] = ();
$zlb_f_mea2[3] = ();

$zlt_b_mea1[3] = ();
$zlt_b_mea2[3] = ();
$zlt_f_mea1[3] = ();
$zlt_f_mea2[3] = ();
=cut

$mdo_b_mea1[3] = ();
$mdo_b_mea2[3] = ();
$mdo_f_mea1[3] = ();
$mdo_f_mea2[3] = ();

$tte_b_mea1[3] = ();
$tte_b_mea2[3] = ();
$tte_f_mea1[3] = ();
$tte_f_mea2[3] = ();

$ttz_b_mea1[3] = ();
$ttz_b_mea2[3] = ();
$ttz_f_mea1[3] = ();
$ttz_f_mea2[3] = ();

$aro_b_mea1[3] = ();
$aro_b_mea2[3] = ();
$aro_f_mea1[3] = ();
$aro_f_mea2[3] = ();

$bkn_b_mea1[3] = ();
$bkn_b_mea2[3] = ();
$bkn_f_mea1[3] = ();
$bkn_f_mea2[3] = ();

$tps_b_mea1[3] = ();
$tps_b_mea2[3] = ();
$tps_f_mea1[3] = ();
$tps_f_mea2[3] = ();

$isu_b_mea1[3] = ();
$isu_b_mea2[3] = ();
$isu_f_mea1[3] = ();
$isu_f_mea2[3] = ();

$itl_b_mea1[3] = ();
$itl_b_mea2[3] = ();
$itl_f_mea1[3] = ();
$itl_f_mea2[3] = ();

$itd_b_mea1[3] = ();
$itd_b_mea2[3] = ();
$itd_f_mea1[3] = ();
$itd_f_mea2[3] = ();

$itt_b_mea1[3] = ();
$itt_b_mea2[3] = ();
$itt_f_mea1[3] = ();
$itt_f_mea2[3] = ();

$upt_b_mea1[3] = ();
$upt_b_mea2[3] = ();
$upt_f_mea1[3] = ();
$upt_f_mea2[3] = ();

$ult_b_mea1[3] = ();
$ult_b_mea2[3] = ();
$ult_f_mea1[3] = ();
$ult_f_mea2[3] = ();

$ypn_b_mea1[3] = ();
$ypn_b_mea2[3] = ();
$ypn_f_mea1[3] = ();
$ypn_f_mea2[3] = ();

$ith_b_mea1[3] = ();
$ith_b_mea2[3] = ();
$ith_f_mea1[3] = ();
$ith_f_mea2[3] = ();

$dpa_b_mea1[3] = ();
$dpa_b_mea2[3] = ();
$dpa_f_mea1[3] = ();
$dpa_f_mea2[3] = ();

$ysr_b_mea1[3] = ();
$ysr_b_mea2[3] = ();
$ysr_f_mea1[3] = ();
$ysr_f_mea2[3] = ();

$chemical_test_date = ();
$lube_date = ();
$lube_weight = ();
$lube_weight_uom = ();
$grade = ();

$zlt = ();
$zlb = ();
$zlt = ();
$zlx = ();
#$dt = ();
#Alex Gerlants added End -------------------------------------------------------

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

#Alex Gerlants added Begin -----------------------------------------------------
$zb = ();
$zca = ();
$zcd = ();
$lb = ();
$sx = ();
$zpb = ();
$zsb = ();
$zsn = ();
$zzr = ();
$zas = ();
$zbi = ();
$zb = ();
$zv = ();
$zco = ();
$ik = ();
$zp = ();
$ss = ();
$sh = ();
#$zv = ();


$tty_b_mea2[4] = ();
$ttu_b_mea2[4] = ();
$tel_b_mea2[4] = ();
$trl_b_mea2[4] = ();
$tnl_b_mea2[4] = ();
$ith_b_mea2[4] = ();
$tts_b_mea2[4] = ();
$ttl_b_mea2[4] = ();
$tto_b_mea2[4] = ();
$ttt_b_mea2[4] = ();
$tnd_b_mea2[4] = ();
$tnt_b_mea2[4] = ();
$tmd_b_mea2[4] = ();
$trd_b_mea2[4] = ();
$trt_b_mea2[4] = ();
$tes_b_mea2[4] = ();
$tet_b_mea2[4] = ();
$thl_b_mea2[4] = ();
$thd_b_mea2[4] = ();
$tht_b_mea2[4] = ();
$x27_b_mea2[4] = ();
$n4t_b_mea2[4] = ();
$n5t_b_mea2[4] = ();
$mdo_b_mea2[4] = ();
$tte_b_mea2[4] = ();
$ttz_b_mea2[4] = ();
$aro_b_mea2[4] = ();
$bkn_b_mea2[4] = ();
$tps_b_mea2[4] = ();
$isu_b_mea2[4] = ();
$itl_b_mea2[4] = ();
$itd_b_mea2[4] = ();
$itt_b_mea2[4] = ();
$upt_b_mea2[4] = ();
$ult_b_mea2[4] = ();
$ypn_b_mea2[4] = ();
$dpa_b_mea2[4] = ();
$ysr_b_mea2[4] = ();
$tty_f_mea2[4] = ();
$ttu_f_mea2[4] = ();
$tel_f_mea2[4] = ();
$trl_f_mea2[4] = ();
$tnl_f_mea2[4] = ();
$ith_f_mea2[4] = ();
$tts_f_mea2[4] = ();
$ttl_f_mea2[4] = ();
$tto_f_mea2[4] = ();
$ttt_f_mea2[4] = ();
$tnd_f_mea2[4] = ();
$tnt_f_mea2[4] = ();
$tmd_f_mea2[4] = ();
$trd_f_mea2[4] = ();
$trt_f_mea2[4] = ();
$tes_f_mea2[4] = ();
$tet_f_mea2[4] = ();
$thl_f_mea2[4] = ();
$thd_f_mea2[4] = ();
$tht_f_mea2[4] = ();
$x27_f_mea2[4] = ();
$n4t_f_mea2[4] = ();
$n5t_f_mea2[4] = ();
$mdo_f_mea2[4] = ();
$tte_f_mea2[4] = ();
$ttz_f_mea2[4] = ();
$aro_f_mea2[4] = ();
$bkn_f_mea2[4] = ();
$tps_f_mea2[4] = ();
$isu_f_mea2[4] = ();
$itl_f_mea2[4] = ();
$itd_f_mea2[4] = ();
$itt_f_mea2[4] = ();
$upt_f_mea2[4] = ();
$ult_f_mea2[4] = ();
$ypn_f_mea2[4] = ();
$dpa_f_mea2[4] = ();
$ysr_f_mea2[4] = ();
#Alex Gerlants added End -------------------------------------------------------

=pod

# $coil_org_num = '2317497'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
# $coil_org_num = '0167505'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
# $coil_org_num = '03458-1'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
# $coil_org_num = '2369391'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

my $skid_count = 0;

#print "Before select count(*). coil_org_num: $coil_org_num  \n";
my $coil_org2 = $coil_org_num;

my $sth = $dbh->prepare(
                "select count(distinct sheet_skid.sheet_skid_num)
                 from   production_sheet_item
                        join ab_job on ab_job.ab_job_num = production_sheet_item.ab_job_num
                        join sheet_skid_detail on sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num
                        join sheet_skid on sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num
                        join coil on coil.coil_abc_num = production_sheet_item.coil_abc_num
                 where  coil.coil_org_num = '$coil_org_num'"
);

$sth->execute() || die "Could not execute SQL! \n ";

$sth->bind_col(1, \$skid_count);

#print "Before while ( $sth->fetch ). skid_count: $skid_count  \n";

while ( $sth->fetch ) {
          #print "Inside while. skid_count: $skid_count  \n";
          $rows++;
}

#print "After while ( $sth->fetch ). skid_count: $skid_count  \n";

#my $skid_count = $counter;

#print "Before sth->finish(). counter: $counter  skid_count: $skid_count  \n";

$sth->finish();

#print "After sth->finish(). counter: $counter  skid_count: $skid_count  \n";

#print "Before IF. skid_count: $skid_count  \n";

# $skid_count = 2; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY


print "skid_count: $skid_count  \n";

if ( $skid_count == 0 ) {
	$string_out = "";
    print "Inside $rowcount == 0. string_out: $string_out  \n";
}
else {
    #print "Insdie Else  \n";

	my $coil_abc_num     = 0;
	#my $coil_org_num     = $lin[9];
	#print "coil_org_num:  $coil_org_num  \n";
	my $ab_job_num       = 0;
	my $sheet_skid_num   = 0;
    my $coil_abc = 0;
    my $coil_org = "";	

    my $status_hold_count = 0;
    my $status_hold_skid_num = "";
	
	
    # $coil_org_num = '2317497'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
    # $coil_org_num = '0167505'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
    # $coil_org_num = '03458-1'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
    # $coil_org_num = '2369391'; #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
	
	my $sth = $dbh->prepare(
         "select distinct coil.coil_abc_num, coil.coil_org_num, sheet_skid.ab_job_num, sheet_skid.sheet_skid_num, sheet_skid.skid_sheet_status
          from   production_sheet_item
                 join ab_job on ab_job.ab_job_num = production_sheet_item.ab_job_num
                 join sheet_skid_detail on sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num
                 join sheet_skid on sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num
                 join coil on coil.coil_abc_num = production_sheet_item.coil_abc_num
         where   coil.coil_org_num = '$coil_org_num'
         order by sheet_skid.ab_job_num, sheet_skid.sheet_skid_num"
	);
	
	$sth->execute() || die "Could not execute SQL! \n ";
	#          || error_report("Error while executing SQL statement from INBOUND_COIL.\n");
	
    my $coil_org = $coil_org_num;
    #my $coil_org2 = $coil_org_num;
	
    #print "Before While. coil_org: $coil_org  \n";

	while (($coil_abc_num, $coil_org_num, $ab_job_num, $sheet_skid_num, $skid_sheet_status) = $sth->fetchrow()) {
    	#print "Inside While. coil_abc_num: $coil_abc_num  coil_org_num: $coil_org_num  ab_job_num: $ab_job_num  sheet_skid_num: $sheet_skid_num  \n";


	   #--------------------------------------------------------------------------------------------------

       #print "Before if (skid_sheet_status eq 16). sheet_skid_num: $sheet_skid_num   skid_sheet_status: $skid_sheet_status  \n";  

       if ($skid_sheet_status eq 16) {

           $status_hold_count = $status_hold_count + 1;

           if ( $status_hold_skid_num eq "" ) {
               $status_hold_skid_num = $sheet_skid_num;
           }
           else  {
               $status_hold_skid_num = $status_hold_skid_num . ", "  . $sheet_skid_num;
           }

           #print "status_hold_skid_num: $status_hold_skid_num  \n";

           #Update sheet_skid.skid_sheet_status from 16-'Hold For Cert' to 2-'Ready'
           $dbh->do( "update sheet_skid
                      set    sheet_skid.skid_sheet_status = 2
                      where  sheet_skid_num = $sheet_skid_num"
           ); #$dbh->do

           $dbh->do("commit");

       }; #if ($skid_sheet_status eq 16)

	   #--------------------------------------------------------------------------------------------------


        $coil_abc = $coil_abc_num;
		$string_out = $string_out . " Job #: " . $ab_job_num . " Skid #: " . $sheet_skid_num . "\n";

        #print "Inside while loop. string_out: $string_out  \n";  

	} #while (($coil_abc_num, $coil_org_num, $ab_job_num, $sheet_skid_num, $skid_sheet_status) = $sth->fetchrow())

    #print "After While. coil_org: $coil_org  \n";

    $string_out = "\n\nCoil ABC #: " . $coil_abc . " Coil Org #: " . $coil_org . ":\n" . $string_out;

    $from = '863_Received';
    $to = 'agerlants@albl.com';   #Development
    #$to = 'EDI_863@albl.com';    #Production
    #$to = 'it-support.com';    #Production

    $subject = '863 received. Skids exist for Coil ABC #:' . $coil_abc . " and Coil Org # " .  $coil_org;
    $message = "Certificate of Conformance EDI (863) received.\nThe following skids exist:" . $string_out;

    if ( $status_hold_count > 0 ) {
        $message = $message . "\n\n" . "The following skids updated from status 'Hold For Cert' to status 'Ready'\n" . $status_hold_skid_num;
    }

    #print "Before open(MAIL). message: $message  to: $to  from: $from  subject: $subject  message: $message  string_out: $string_out  \n";

    $query = "select email_address from auto_report_emails where report_name = 'hold for cert email' order by email_address desc";
    my $addr = "";
    my $email_string = "";
    $dth = $dbh->prepare($query);
    $dth->execute();

    #Build list of emails
    while (  $addr = $dth->fetchrow() ) {
        $email_string = $email_string . "\n" . $addr;
    }

    # print "email_string: $email_string  \n";
    $message = $message . "\n\nEmails sent to the following addresses:\n" . $email_string;

    $dth = $dbh->prepare($query);
    $dth->execute();

    while (  $addr = $dth->fetchrow() ) {
        # print "addr: $addr   \n";
        $to = $addr;

        # $addr = "agerlant@hotmail.com";
        # print "addr: $addr   \n";

        my $param = "'$from', '$to', '$subject', '$message'";
        print "param hold for cert email: $param  \n";
        my $cmd = "/templar/templar/util/send_email.pl $param";
        print "cmd hold for cert email: $cmd  \n";
        my $result = system($cmd);
    }

	$sth->finish();
} # Else for "if ( $skid_count == 0 )"

=cut

#print "string_out: $string_out  \n";

# print "Before if (ok_997 eq 'yes'). coil_org2: $coil_org2  coil_org: $coil_org  \n";

if ($ok_997 eq "yes") {
   # print ("Inside 997 if. skid_count: $skid_count  \n");
   my $cmd = "/templar/templar/util/997_863.pl " . $edi_file;
   system($cmd);   #Send 997


   # $skid_count = 0;  #TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

   #Don't send email if skids exist for $coil_org2. That email is sent above
   if ( $skid_count == 0 ) {
      $from = '863_const_db_2.pl';
#      $to = 'agerlants@albl.com';
      $subject = "Coil Org: $coil_org2 EDI 863 received 997 created.";
      $message = "Coil Org: $coil_org2 EDI 863 file $edi_file received  successfully. 997 for file $edi_file created successfully.";

#      print "Before query = .  subject: $subject  message: $message  \n";

      $query = "select email_address from auto_report_emails where report_name = 'received 863' order by email_address desc";
      my $addr = "";
      $dth = $dbh->prepare($query);
	  $dth->execute();
	
#      print "#################### Before While loop  \n";

	  while (  $addr = $dth->fetchrow() ) {
          # print "addr: $addr   \n";
          $to = $addr; 
#          print "Inside While loop. coil_org2: $coil_org2  from: $from  to: $to  subject: $subject  message: $message \n";

          @ARGV = ($from, $to, $subject, $message);

#          print "\nOriginal args:\n";
#          for my $arg (@ARGV) {
#            print "$arg\n";
#          }

          # eval { require "send_email.pl" };
          # my $result = capture($^X, send_email.pl @ARGV);
          # my $result = send_email.pl $to, $from, $subject, $message;

          # my $param = @ARGV;
          my $param = "'$from', '$to', '$subject', '$message'";
#          print "param: $param  \n";
          my $cmd = "/templar/templar/util/send_email.pl $param";
#          print "cmd: $cmd  \n";
          my $result = system($cmd);

#          print "result: $result  \n";
      }
   } #if ( $skid_count == 0 )


}; #End of if ($ok_997 eq "yes")


#print (" coil $lin[9] coil_org: $coil_org   done \n");
}   #End of one coil

} #End of if it is Alcan 863

