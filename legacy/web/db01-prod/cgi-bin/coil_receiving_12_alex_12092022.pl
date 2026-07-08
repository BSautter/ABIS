#!/usr/local/bin/perl
use DBI;
use IO::Socket;
use DBD::Oracle;
use CGI qw/:standard/;
use File::Basename;
use Net::Ping;
use Switch;

$db       = "dbi:Oracle:host=db01;sid=abc11; port=1523";
$username = "dbo";
$passwd   = "__DB_PASSWORD_REDACTED__";
$cgi = new CGI;
$Printer_Add = '192.168.10.12';
$deviceAddress = remote_addr();

switch ($deviceAddress){
   case '192.168.10.8' {$Printer_Add = '192.168.10.12';}
   case '192.168.10.9' {$Printer_Add = '192.168.10.13';}
   case '192.168.10.10' {$Printer_Add = '192.168.10.14';}
}

if ( param('coil_org_num') ) {
        $coil_org_num_data = param('coil_org_num');
        &coil_exist_check($coil_org_num_data);
}

elsif ( param('coil_detail') ) {
        $coil_detail_data = param('coil_detail');
        &coil_detail($coil_detail_data);        #show coil detail data
}

elsif ( param('print_label') ) {
        $print_label_data = param('print_label');
        &print_label($print_label_data);        #print coil ABC labels
}

elsif ( param('email') ) {
        $email_data = param('email');
        &send_email($email_data);       #send email notification coil defect
}

elsif ( param('print_label_for_coil_abc_num') ) {
        $print_label_for_coil_abc_num_data = param('print_label_for_coil_abc_num');
        &print_label_for_coil_abc_num($print_label_for_coil_abc_num_data);      #print coil ABC labels for entered coil ABC number
}
elsif ( param('qrscan') ) {
		$coil_org_qr_code = param('coil_qr_code');
		$coil_org_num_data = param('qrscan');
		&addqrcode($coil_org_num_data,$coil_org_qr_code);

}

else {
        &print_html;    #starting web page
}
exit(0);

sub print_html {
        print "Content-type: text/html\n\n";

        print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >

<head>
    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
</head>
<body>
    <p>
        <FORM id="coil_input" ACTION="coil_receiving_12.pl" METHOD="post">
                <b>Customer Coil Number: </b>
                        </p>
                        <P>
                        <INPUT type="password" id="coil_org_num" NAME="coil_org_num" VALUE="">
                        </p>
                        <P>
                        &nbsp;
                        </p>
                        <P>
                                <INPUT TYPE="submit" VALUE="Get Coil Details"> <br><br>
                                <Input Type="reset" value="Reset">
                        </p>
                </FORM>

                <script type="text/javascript">
                        document.getElementById("coil_org_num").focus();
                </script>
    </p>
</body>
</html>
ENDOFTEXT
        exit(0);
}

sub coil_exist_check ($){
        my $coil_org_num = $_[0];



        if ($coil_org_num =~ /^S/) {
        $coil_org_num = substr($coil_org_num,1); # remove coil barcode header (S)
        }
        else {
        #$coil_org_num = substr($coil_org_num,1)." is NOT valid.";
        my $coil_org_num = $_[0];
        }

        $ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
        $dbh = DBI->connect( $db, $username . '/' . $passwd )
          || error_report("Error connecting to database\n");

        my $coil_abc_num;

        my $sth = $dbh->prepare(
                "SELECT         INBOUND_COIL_STATUS.COIL_ABC_NUM
                FROM INBOUND_COIL_STATUS
                WHERE INBOUND_COIL_STATUS.COIL_NUMBER = '$coil_org_num' and
                        INBOUND_COIL_STATUS.COIL_ABC_NUM > 0"
        );

        $sth->execute()
          || error_report("Error while executing SQL statement from INBOUND_COIL_STATUS.\n");

        $sth->bind_columns(
                undef,     \$coil_abc_num
        );
        $rowct = 0;
        while ( $sth->fetch() ) {
                $rowct++;
        }
        $sth->finish();

        if ( $rowct == 0 ) {
                &coil_detail($coil_org_num_data);
        }
        else {
                print "Content-type: text/html\n\n";

                print <<ENDOFTEXT;
                <html xmlns="http://www.w3.org/1999/xhtml" >
                <head>
                    <title>Aluminum Blanking Co.--- Coil Receiving 12</title>
                </head>
                <body>

                    <p>

ENDOFTEXT
                print '<b>Barcode label of Coil ABC #: '.$coil_abc_num.' has been printed for '.$coil_org_num. '. Please choose:</b>';
                print <<ENDOFTEXT;
        <FORM id="coil_org_num" ACTION="coil_receiving_12.pl" METHOD="post">
                        </p>
                        <P>
ENDOFTEXT
                print '<INPUT type="hidden"  NAME="coil_detail" VALUE="S'.$coil_org_num.'">';
print <<ENDOFTEXT;
                        </p>
                        <P>
                                <INPUT TYPE="submit" VALUE="New Coil ABC Num">
                        </p>
                </FORM>
                <p> or </p>
ENDOFTEXT
                print <<ENDOFTEXT;
        <FORM id="coil_abc_num" ACTION="coil_receiving_12.pl" METHOD="post">
                        </p>
                        <P>
ENDOFTEXT

                print '<INPUT type="hidden" NAME="print_label_for_coil_abc_num" VALUE="'.$coil_abc_num.'">';
print <<ENDOFTEXT;

                        </p>
                        <p>
                                <INPUT TYPE="submit" VALUE="Reprint Labels">
                        </p>
                </FORM>
                <FORM ACTION="coil_receiving_12.pl" METHOD="post">
                        </p> 
                        <p>
                <INPUT type="password" id="coil_qr_code" NAME="coil_qr_code" VALUE="">
                <INPUT type="hidden" NAME="qrscan" VALUE="$coil_org_num">
                <INPUT type="submit" VALUE="Rescan QR Code">

                </p>
                </FORM>

         </p>
                        <FORM ACTION="coil_receiving_12.pl" METHOD="post">
                                <input type="submit" value="HOME" />
                        </FORM>
                </body>
                </html>
ENDOFTEXT
        }
}

sub coil_detail ($) {
        my $coil_org_num = $_[0];

        if ($coil_org_num =~ /^S/) {
        $coil_org_num = substr($coil_org_num,1); # remove coil barcode header (S)
        }
        else {
        #$coil_org_num = substr($coil_org_num,1)." is NOT valid.";
        my $coil_org_num = $_[0];
        }

        if ($coil_org_num eq "000000") {
                $coil_org_num = "NO BARCODE";
        }

        $ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
        $dbh = DBI->connect( $db, $username . '/' . $passwd )
          || error_report("Error connecting to database\n");

        my $BOL          = "";
        my $NET_WEIGHT   = "";
        my $GROSS_WEIGHT = "";
        my $ALLOY        = "";
        my $TEMPER       = "";
        my $GAUGE        = "";
        my $WIDTH        = "";
        my $LOT_NUM      = "";
        my $PACK_ID      = "";

        my $sth = $dbh->prepare(
                "SELECT         INBOUND_COIL.BOL,
                                INBOUND_COIL.NET_WEIGHT,
                                INBOUND_COIL.GROSS_WEIGHT,
                                INBOUND_COIL.ALLOY,
                                INBOUND_COIL.TEMPER,
                                INBOUND_COIL.COIL_GAUGE,
                                INBOUND_COIL.COIL_WIDTH,
                                INBOUND_COIL.LOT,
                                INBOUND_COIL.PACK_ID
                FROM INBOUND_COIL
                WHERE INBOUND_COIL.COIL_NUMBER = '$coil_org_num'"
        );

        $sth->execute()
          || error_report("Error while executing SQL statement from INBOUND_COIL.\n");

        $sth->bind_columns(
                undef,     \$BOL,    \$NET_WEIGHT, \$GROSS_WEIGHT,
                \$ALLOY,   \$TEMPER, \$GAUGE,      \$WIDTH,
                \$LOT_NUM, \$PACK_ID
        );
        $rowct = 0;
        while ( $sth->fetch() ) {
                $rowct++;
        }
        $sth->finish();

        #print $rowct;

        if ( $rowct == 0 ) {
                $BOL          = "NONE";
                $NET_WEIGHT   = "NONE";
                $GROSS_WEIGHT = "NONE";
                $ALLOY        = "NONE";
                $TEMPER       = "NONE";
                $GAUGE        = "NONE";
                $WIDTH        = "NONE";
                $LOT_NUM      = "NONE";
                $PACK_ID      = "NONE";
        }

        #print header;
        #print $cgi->header;
        #print $cgi->header( -expires => 'now' );
        print "Content-type: text/html\n\n";

        print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >
<head>
    <title>Aluminum Blanking Co.--- Coil Receiving 12</title>
</head>
<body>
ENDOFTEXT
print '<b>Coil ID:  </b>'.$coil_org_num.'</br>';
print '<b>BOL:  </b>'.$BOL.'</br>';
print '<b>Coil Net Weight:  </b>'.$NET_WEIGHT.'</br>';
print '<b>Coil Gross Weight:  </b>'.$GROSS_WEIGHT.'</br>';
print '<b>Alloy:  </b>'.$ALLOY.'</br>';
print '<b>Temper:  </b>'.$TEMPER.'</br>';
print '<b>Gauge:  </b>' .$GAUGE.'</br>';
print '<b>Width:  </b>' .$WIDTH.'</br>';
print '<b>Lot Num:  </b>'.$LOT_NUM.'</br>';
print '<b>Pack ID:  </b>'.$PACK_ID.'</br>';

print <<ENDOFTEXT;
                <FORM ACTION="coil_receiving_12.pl" METHOD="post">

ENDOFTEXT
        print '<INPUT type="hidden" NAME="print_label" VALUE="'.$coil_org_num.'">';
        print <<ENDOFTEXT;
                <input type="submit" value="Print Label" />
                </FORM>
                <FORM ACTION="coil_receiving_12.pl" METHOD="post">
                <INPUT type="password" id="coil_qr_code" NAME="coil_qr_code" VALUE="">
ENDOFTEXT
        
		print '<INPUT type="hidden" NAME="qrscan" VALUE="'.$coil_org_num.'">';
        print <<ENDOFTEXT;
                <input type="submit" value="Add QR CODE" />
                </FORM>
				<script type="text/javascript">
                        document.getElementById("coil_qr_code").focus();
                </script>
                       <FORM ACTION="coil_receiving_12.pl" METHOD="post">
                                <input type="submit" value="HOME" />
                        </FORM>
</body>
</html>
ENDOFTEXT
}

sub print_label ($){
        my $coil_org_num = $_[0];
        my $coil_abc_num = 0;

        my $p = Net::Ping->new('udp');
        #check label pinter connection
        if ($p->ping($Printer_Add)) {
                $p->close();
        $ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
                $dbh = DBI->connect( $db, $username . '/' . $passwd )
                  || error_report("Error connecting to database\n");

                my $sth = $dbh->prepare("SELECT COIL_ABC_NUM_SEQ.NEXTVAL FROM DUAL");

                $sth->execute()|| error_report("Error while executing SQL statement from COIL_ABC_NUM_SEQ.\n");

                $coil_abc_num = $sth->fetchrow();


                $dbh->do("UPDATE INBOUND_COIL_STATUS
                                                                SET COIL_ABC_NUM = $coil_abc_num
                                                                WHERE COIL_NUMBER = '$coil_org_num'");

                $dbh->do("COMMIT");

               my $Printer_Socket = new IO::Socket::INET -> new ($Printer_Add.":6101")|| error_report("Error while creating printer socket.\n");
				my $abc_coil = '';
				$abc_coil = $coil_abc_num;
				$Printer_Socket->autoflush(1);
                for ( $count = 2 ; $count >= 1 ; $count-- ) {
					my $line = '';
					$line .="^XA";
					$line .="^MNA";
					$line .="^MMK";
					$line .="^PW384";
					$line .="^LL0203";
					$line .="^LS0";
					$line .="^BY3,3,50^FT365,78^BCI,,N,N";
					$line .="^FD".$abc_coil."^FS";	
					$line .="^FT375,150^A0I,25,33^FH\^FDCoil ABC #: ".$abc_coil."^FS";	
					$line .="^FO69,20^GB138,0,5^FS";
					$line .="^FT376,25^A0I,20,26^FH\^FDINSPECTED BY:^FS";
					$line .="^PQ1,0,1,Y";
					$line .="^XZ";
						
				print $Printer_Socket $line;
				my $dontCare = <Printer_Socket>;
				}
					
                close($Printer_Socket);
                #&print_html();
                        print "Content-type: text/html\n\n";

                print <<ENDOFTEXT;
                <html xmlns="http://www.w3.org/1999/xhtml" >

                <head>
                    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
                </head>
                <body>
                        <p>
                        Print label completed!
                        </P>
                        <FORM>
                                <INPUT type="button" value="OK" onClick="history.back()">
                        </FORM>
                </body>
                </html>
ENDOFTEXT
                
 }
    else {
        $p->close();

        print "Content-type: text/html\n\n";

                print <<ENDOFTEXT;
                <html xmlns="http://www.w3.org/1999/xhtml" >

                <head>
                    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
                </head>
                <body>
                        <p>
                        Label printer is not connected, please try again.
                        </P>
                        <FORM>
                                <INPUT type="button" value="OK" onClick="history.back()">
                        </FORM>
                </body>
                </html>
ENDOFTEXT
    }
}

sub print_label_for_coil_abc_num ($){
        my $coil_abc_num = $_[0];
        my $abc_coil = $coil_abc_num;
        my $p = Net::Ping->new('udp');
        #check label pinter connection
        if ($p->ping($Printer_Add)) {
                $p->close();
                        
        my $Printer_Socket = new IO::Socket::INET -> new ($Printer_Add.":6101")|| error_report("Error while creating printer socket.\n");
		$Printer_Socket->autoflush(1);
                for ( $count = 2 ; $count >= 1 ; $count-- ) {
					my $line = '';
					$line .="^XA";
					$line .="^MNA";
					$line .="^MMK";
					$line .="^PW384";
					$line .="^LL0203";
					$line .="^LS0";
					$line .="^BY3,3,50^FT365,78^BCI,,N,N";
					$line .="^FD".$abc_coil."^FS";	
					$line .="^FT375,150^A0I,25,33^FH\^FDCoil ABC #: ".$abc_coil."^FS";	
					$line .="^FO69,20^GB138,0,5^FS";
					$line .="^FT376,25^A0I,20,26^FH\^FDINSPECTED BY:^FS";
					$line .="^PQ1,0,1,Y";
					$line .="^XZ";
						
				print $Printer_Socket $line;
				my $dontCare = <Printer_Socket>;
				}
        close($Printer_Socket);
        &print_html();
        } 
        else {
                $p->close();
                        print "Content-type: text/html\n\n";

                print <<ENDOFTEXT;
                <html xmlns="http://www.w3.org/1999/xhtml" >

                <head>
                    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
                </head>
                <body>
                        <p>
                        Label printer is not connected, please try again.
                        </P>
                        <FORM>
                                <INPUT type="button" value="OK" onClick="history.back()">
                        </FORM>
                </body>
                </html>
ENDOFTEXT
                }
            

}

sub send_email ($) {
        my $coil_org_num = $_[0];

        $ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
        $dbh = DBI->connect( $db, $username . '/' . $passwd )
          || error_report("Error connecting to database\n");

        my $sth = $dbh->prepare("BEGIN DBO.P_SEND_EMAIL_COIL_DEFECT('$coil_org_num'); END;");

        $sth->execute()|| error_report("Error while executing P_SEND_EMAIL_COIL_DEFECT.\n");

        &email_sent_message();
}

sub email_sent_message {
        print "Content-type: text/html\n\n";

        print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >

<head>
    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
</head>
<body>
        <p>
        Defect Notification Email Has Been Sent.
        </P>
        <FORM>
                <INPUT type="button" value="OK" onClick="history.back()">
        </FORM>
</body>
</html>
ENDOFTEXT
}

sub addqrcode  {
	my $coil_org_num = shift;
	my $coil_qr_code = shift;
        my $substr = '$';
        my $coil_org_num_count = () = $coil_qr_code =~ /$coil_org_num/g;

#Alex Gerlants. 12/09/2022. IT Request 1737. Added "  && (substr($coil_qr_code, 0, 2) eq "04")  ". Check the first 2 characters
#Alex Gerlants. 12/09/2022. IT Request 1737. Changed from "> 67" to "> 71"
 if ( (substr($coil_qr_code, 0, 2) eq "04") && (length($coil_qr_code) > 71) && (index($coil_qr_code, $substr) != -1) && (length($coil_org_num) > 2) && $coil_org_num_count == 1) {
 
        $ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
                $dbh = DBI->connect( $db, $username . '/' . $passwd )
                  || error_report("Error connecting to database\n");

                 $dbh->do("MERGE INTO BARCODE_STRING D
                        USING (SELECT '$coil_org_num' COIL_ORG_NUM, '$coil_qr_code' BARCODE_STRING FROM DUAL) S
                        ON (D.COIL_ORG_NUM = S.COIL_ORG_NUM)
                        WHEN MATCHED THEN UPDATE SET D.BARCODE_STRING = S.BARCODE_STRING
                        WHEN NOT MATCHED THEN INSERT (D.COIL_ORG_NUM, D.BARCODE_STRING)
                        VALUES (S.COIL_ORG_NUM, S.BARCODE_STRING)");
                 $dbh->do("COMMIT");
                 $dbh->do("UPDATE INBOUND_COIL_STATUS
                                                                SET BARCODE_STRING = '$coil_qr_code'
                                                                WHERE COIL_NUMBER = '$coil_org_num'");
                $dbh->do("COMMIT");
 
        print "Content-type: text/html\n\n";
        print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >
<head>
    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
</head>
<body>
ENDOFTEXT
        print '<b>QR Code saved for Coil ORG #: '.$coil_org_num.' qr code: '.$coil_qr_code. '.</b>';
print <<ENDOFTEXT;
        <FORM>
                <INPUT type="button" value="OK" onClick="history.back()">
        </FORM>
</body>
</html>
ENDOFTEXT
         }
         else {
        print "Content-type: text/html\n\n";
        print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >
<head>
    <title>Aluminum Blanking Co.- Coil Receiving 12</title>
</head>
<body>
ENDOFTEXT
        print '<b>Invalid QR Code. Please re-scan.</b>';
print <<ENDOFTEXT;
        <FORM>
                <INPUT type="button" value="OK" onClick="history.back()">
        </FORM>
</body>
</html>
ENDOFTEXT
                 }
                 


        exit(0);
}

sub error_report {

# Report error
        print "$_[0]\n";
        sendEmail("support\@albl.com", "server\@albl.com", "Coil Receiving Error", "Coil receiving error: $_[0]\n");
        exit;
}

sub sendEmail {
        
        my($to, $from, $subject, $message) = @_;
        my $sendmail = 'usr/sbin/sendmail';
        open(MAIL, "|$sendmail -oi -t");
        print MAIL "From: $from\n";
        print MAIL "To: $to\n";
        print MAIL "Subject: $subject\n\n";
        print MAIL "message\n";
        close(MAIL);
        exit; 
}



END {
        $dbh->disconnect if defined($dbh);
}
