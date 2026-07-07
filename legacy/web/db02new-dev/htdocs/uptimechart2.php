<?php
# initialize object with chart type
$gdc = new gdchart(gdchart::bar);

# add data points
$gdc->addValues(array(18234, 16484, 16574, 17464, 19474));

# add X-axis labels
$gdc->setLabels(array('2002', '2003', '2004', '2005', '2006'));

# generate PNG chart and send to client
header('Content-Type:image/png');
echo $gdc->out(300,200,GDChart::PNG);
?>
