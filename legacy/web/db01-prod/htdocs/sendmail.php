<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">

<html>
<head>
<meta http-equiv="Content-Type" content="text/html; charset=utf-8" />
<title>Sendemail Script</title>
</head>
<body>

<?php
$requested_by = $_POST["requested_by"];
$reply_to_email = $_POST["reply_to_email"];
$subject = $_POST["subject"];
$cc = $_POST["cc"];
$department = $_POST["department"];
$type_of_request1 = $_POST["type_of_request1"];
$type_of_request2 = $_POST["type_of_request2"];
$type_of_request2 = "/$type_of_request2";
$detail_description_of_issue = $_POST["detail_description_of_issue"];
$areas_of_impact = $_POST["areas_of_impact"];
$risk_level = $_POST["risk_level"];
$estimated_cost_of_quality_value = $_POST["estimated_cost_of_quality_value"];

$ccs = split(";", $cc);

$send_to = "it-support@albl.com";

$headers = "From: $reply_to_email";
foreach ($ccs as $cc_item) {
	$headers .= "\r\nCc:".$cc_item;
}

echo $replytoemail;


if (!strstr($reply_to_email,"@albl.com"))
{
echo "<h2>Please enter a valid albl.com e-mail address</h2>\n";
$badinput = "<h2>Request was NOT submitted</h2>\n";
echo $badinput;
die ("<h2>Use browser's BACK button to go back to the request form! </h2>");
}

if (empty($reply_to_email) || empty($requested_by) || empty($subject) || empty($department) || empty($detail_description_of_issue) || empty($areas_of_impact) || empty($estimated_cost_of_quality_value)) {
echo "<h2>Please fill in all required fields.</h2>\n";
$badinput = "<h2>Request was NOT submitted</h2>\n";
echo $badinput;
die ("<h2>Use browser's BACK button to go back to the request form! </h2>");
}

$todayis = date("l, F j, Y, g:i a") ;

$subject = "Web Support Requested by: $requested_by. Subject: $subject.";

$message = "$todayis [EST] \n
Request By: $requested_by \n
Reply to E-mail: $reply_to_email \n
Department: $department \n
Type(s) of Request: $type_of_request1 $type_of_request2 \n
Detail Description of Issue: $detail_description_of_issue \n
Area(s)of Impact : $areas_of_impact \n
Risk Level: $risk_level \n
Estimated Cost of Quality Value : $estimated_cost_of_quality_value \n
";

mail($send_to, $subject, $message, $headers);
?>

<p align="center">
<h4>Date: <?php echo $todayis ?> </h4>
<br/>
<h3>Thank You,  <?php echo $requested_by ?></h3>
<h3>Your request has been sent to system department.</h3>
</p>

<!--
<form method="post">
<input type="button" value="Close"
onclick="window.close()">
</form>
-->
</body>
</html>
