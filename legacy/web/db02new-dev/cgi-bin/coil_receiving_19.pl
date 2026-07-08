#!/usr/local/bin/perl
use DBI;
use IO::Socket;
use DBD::Oracle;
use CGI qw/:standard/;
use File::Basename;
use Net::Ping;

$db       = "dbi:Oracle:host=db02new;sid=abc01";
$username = "dbo";
$passwd   = "__DB_PASSWORD_REDACTED__";
$cgi = new CGI;
$Printer_Add = '192.168.10.19';

if ( param('coil_org_num') ) {
	$coil_org_num_data = param('coil_org_num');
	&coil_exist_check($coil_org_num_data);
}

elsif ( param('coil_detail') ) {
	$coil_detail_data = param('coil_detail');
	&coil_detail($coil_detail_data);	#show coil detail data	
}

elsif ( param('print_label') ) {
	$print_label_data = param('print_label');
	&print_label($print_label_data);	#print coil ABC labels	
}

elsif ( param('email') ) {
	$email_data = param('email');
	&send_email($email_data);	#send email notification coil defect	
}

elsif ( param('print_label_for_coil_abc_num') ) {
	$print_label_for_coil_abc_num_data = param('print_label_for_coil_abc_num');
	&print_label_for_coil_abc_num($print_label_for_coil_abc_num_data);	#print coil ABC labels for entered coil ABC number	
}

else {
	&print_html;	#starting web page	
}
exit(0);

sub print_html {
	print "Content-type: text/html\n\n";

	print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >

<head>
    <title>Aluminum Blanking Co.- Coil Receiving</title>
</head>
<body>
    <p>
        <FORM id="coil_input" ACTION="coil_receiving_19.pl" METHOD="post">
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
	$coil_org_num = substr($coil_org_num,1)." is NOT valid.";
	}
			
	$ENV{ORACLE_HOME} = '/apps/oracle/product/9.2.0.1.0';
	$dbh = DBI->connect( $db, $username . '/' . $passwd )
	  || error_report("Error connecting to database\n");
	  
	my $coil_abc_num; 

	my $sth = $dbh->prepare(
		"SELECT 	INBOUND_COIL_STATUS.COIL_ABC_NUM         
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
		    <title>Aluminum Blanking Co.--- Coil Receiving</title>
		</head>
		<body>
		
		    <p>
		        
ENDOFTEXT
		print '<b>Barcode label of Coil ABC #: '.$coil_abc_num.' has been printed for '.$coil_org_num. '. Please choose:</b>';
		print <<ENDOFTEXT;
        <FORM id="coil_org_num" ACTION="coil_receiving_19.pl" METHOD="post">
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
        <FORM id="coil_abc_num" ACTION="coil_receiving_19.pl" METHOD="post">
 		 	</p>
		 	<P>
ENDOFTEXT
		
		print '<INPUT type="hidden" NAME="print_label_for_coil_abc_num" VALUE="'.$coil_abc_num.'">';
print <<ENDOFTEXT;
		
		 	</p>
		 	<P>
		 		<INPUT TYPE="submit" VALUE="Reprint Labels">
		 	</p>
 		</FORM>
 		
 	 </p>
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
	$coil_org_num = substr($coil_org_num,1)." is NOT valid.";
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
		"SELECT 	INBOUND_COIL.BOL,					  
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
		$BOL          = "-Not Found-";
		$NET_WEIGHT   = "-Not Found-";
		$GROSS_WEIGHT = "-Not Found-";
		$ALLOY        = "-Not Found-";
		$TEMPER       = "-Not Found-";
		$GAUGE        = "-Not Found-";
		$WIDTH        = "-Not Found-";
		$LOT_NUM      = "-Not Found-";
		$PACK_ID      = "-Not Found-";
	}

	#print header;
	#print $cgi->header;
	#print $cgi->header( -expires => 'now' );
	print "Content-type: text/html\n\n";

	print <<ENDOFTEXT;
<html xmlns="http://www.w3.org/1999/xhtml" >
<head>
    <title>Aluminum Blanking Co.--- Coil Receiving</title>
</head>
<body>

    <p>
        <b>
        Customer Coil Number:
        </b>
        <table>
            <tr>      
ENDOFTEXT
	print '<td><b>' . $coil_org_num . '</b></td>';
	print <<ENDOFTEXT;
            </tr>
        </table>
    </p>
ENDOFTEXT

print '<b>BOL:  </b>'.$BOL.'</br>';
print '<b>Coil Net Weight:  </b>'.$NET_WEIGHT.'</br>';
print '<b>Coil Gross Weight:  </b>'.$GROSS_WEIGHT.'</br>';
print '<b>Alloy:  </b>'.$ALLOY.'</br>';
print '<b>Temper:  </b>'.$TEMPER.'</br>';
print '<b>Gauge:  </b>'	.$GAUGE.'</br>';
print '<b>Width:  </b>' .$WIDTH.'</br>';
print '<b>Lot Num:  </b>'.$LOT_NUM.'</br>';
print '<b>Pack ID:  </b>'.$PACK_ID.'</br>';

print <<ENDOFTEXT;
		<FORM ACTION="coil_receiving_19.pl" METHOD="post"> 		
 		
ENDOFTEXT
        print '<INPUT type="hidden" NAME="print_label" VALUE="'.$coil_org_num.'">';
        print <<ENDOFTEXT;
		<p>
		<input type="submit" value="Print Label" />
 		</p>
 		</FORM>
 		<FORM ACTION="coil_receiving_19.pl" METHOD="post"> 		
 		
ENDOFTEXT
        print '<INPUT type="hidden" NAME="email" VALUE="'.$coil_org_num.'">';
        print <<ENDOFTEXT;
		<p>
		<input type="submit" value="Defects Notification" />
 		</p>
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
		  	
		for ( $count = 2 ; $count >= 1 ; $count-- ) {
			print $Printer_Socket "! 0 200 200 240 1\n";
			print $Printer_Socket "LABEL\n";
			print $Printer_Socket "CONTRAST 2\n";
			print $Printer_Socket "TONE 0\n";
			print $Printer_Socket "SPEED 5\n";
			print $Printer_Socket "PAGE-WIDTH 380\n";
			print $Printer_Socket "BAR-SENSE\n";
			print $Printer_Socket "T Ari12pt.cpf 0 32 20 COIL ABC #:  ". $coil_abc_num."\n";
			print $Printer_Socket "B 39 2 1 50 16 73 2S" . $coil_abc_num."\n";
			print $Printer_Socket "T Ari09pt.cpf 0 21 145 INSPECTED BY:___________\n";
			print $Printer_Socket "FORM\n";
			print $Printer_Socket "PRINT\n";		
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
		    <title>Aluminum Blanking Co.- Coil Receiving</title>
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
	
	my $Printer_Socket = new IO::Socket::INET -> new ($Printer_Add.":6101")|| error_report("Error while creating printer socket.\n");
	  	
	for ( $count = 2 ; $count >= 1 ; $count-- ) {
		print $Printer_Socket "! 0 200 200 240 1\n";
		print $Printer_Socket "LABEL\n";
		print $Printer_Socket "CONTRAST 2\n";
		print $Printer_Socket "TONE 0\n";
		print $Printer_Socket "SPEED 5\n";
		print $Printer_Socket "PAGE-WIDTH 380\n";
		print $Printer_Socket "BAR-SENSE\n";
		print $Printer_Socket "T Ari12pt.cpf 0 32 20 COIL ABC #:  ". $coil_abc_num."\n";
		print $Printer_Socket "B 39 2 1 50 16 73 2S" . $coil_abc_num."\n";
		print $Printer_Socket "T Ari09pt.cpf 0 21 145 INSPECTED BY:___________\n";
		print $Printer_Socket "FORM\n";
		print $Printer_Socket "PRINT\n";		
	}
	close($Printer_Socket);
	&print_html();
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
    <title>Aluminum Blanking Co.- Coil Receiving</title>
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

sub error_report {

# Report error
	print "$_[0]\n";
	exit;
}    

END {
	$dbh->disconnect if defined($dbh);
}
