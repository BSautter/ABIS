#!/usr/local/bin/perl
use strict;
use warnings;

#	Author - Patrick Reynolds
#	Date - December 21, 2016
#	Purpose - To be run on DB02 (or other development servers) to convert the header data in 
#	test EDI's being sent to Novelis from the production information to test/dev information.
#	This prevents test EDI's from accidentally ending up in Novelis production data.
#	Assumptions - that all of the element separators are '*'.

while (<>) {

#	This takes care of all the changes needed in the ISA header.
	s/039630926T/2NDSFTP   /;	# Spaces needed here.
	s/0015049350011G/NOVLSTEST     /;    # Spaces needed here.
	s/\*0[19]\*/*ZZ*/g;
	s/\*P\*/*T*/;

#	This takes care of all the changes needed on the GS segment.
	s/039630926T/2NDSFTP/;	# No spaces needed here.
	s/\*001504935\*/*NOVLTEST*/;	# No spaces needed here.
	print;
	
}
