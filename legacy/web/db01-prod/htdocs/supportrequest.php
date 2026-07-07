<!DOCTYPE HTML PUBLIC "-//W3C//DTD HTML 4.01 Transitional//EN">
<html>
<head>
<meta http-equiv="Content-Type" content="text/html; charset=utf-8" />
<title>IT Support Request Form </title>
</head>
<body>

<!--<form action="MAILTO:vhuang@albl.com" method="post" enctype="text/plain">  -->
<form action="sendmail.php" method="post">

<!-- <img alt="ABC1" src="ABC1.jpg" width="50" height="50"> -->
<!-- <h3 style="font-family:verdana">Aluminum Blanking Co. IT. Support Request Form</h3> -->
<!-- <hr/>  -->

<b>Your Name:</b><font color=red><sup>*</sup></font> <br />
<input type="text" name="requested_by" size="35" />
<br />
<b>Your Email:</b><font color=red><sup>*</sup></font><br />
<input type="text" name="reply_to_email" size="35" />
<br />
<b>Subject:</b><font color=red><sup>*</sup></font><br />
<input type="text" name="subject" size="80" />
<br />
<b>Cc:</b><br />
<input type="text" name="cc" size="80" /><br />
<sub>Please separate email addresses with ;</sub><br />
<br />
<b>Department:</b><font color=red><sup>*</sup></font><br />
<select name="department" size="1">
<option value=" Administration ">Administration</option>
<option value=" Finance ">Finance </option>
<option value=" Human Resource">Human Resource </option>
<option value=" Maintenance ">Maintenance </option>
<option value=" Plant">Plant </option>
<option value=" Production Control">Production Control </option>
<option value=" Purchasing ">Purchasing </option>
<option value=" Sales">Sales </option>
<option value=" Shipping">Shipping </option>
</select>
<br /><br/>

<b>Type(s) of Request:</b><br />
<input id="Misc. Issues" name="type_of_request1" value="Misc. Issues" type="checkbox">Misc. Issues
<input id="Bugs" name="type_of_request2" value="Bugs" type="checkbox">Bugs
<br /><br/>

<b>Detail description of issue:</b><font color=red><sup>*</sup></font>
<br />
<textarea name="detail_description_of_issue" rows="4" cols="80"></textarea>
<br />
<b>Area(s) of Impact (Such as Departments, procedure and process):</b><font color=red><sup>*</sup></font>
<br />
<textarea name="areas_of_impact" rows="4" cols="80"></textarea>
<br />
<br/>

<b>Risk level:</b><br />
<input id="Minor"  type="radio" name="risk_level" value="Minor" checked="checked">Minor
<input id="Medium" type="radio" name="risk_level" value="Medium">Medium
<input id="Major" type="radio"name="risk_level" value="Major" >Major
<br /><br/>

<b>Estimated cost of quality value:</b><font color=red><sup>*</sup></font>
<br />
<textarea name="estimated_cost_of_quality_value" rows="4" cols="80"></textarea>
<br />
<br/>
<hr/>
<input type="submit" value="Send" >
<input type="reset" value="Reset">
<br /><br/>
</form>

</body>
</html>
