#!/usr/local/bin/perl

use strict;
use warnings;

my $list_of_files = $ARGV[0];
my $first_script_name = "GXS2.ksh";
my $second_script_name = "GXS_DELETE_FROM_VAN.ksh";

create_first_script();
open (my $ifh, '<', $list_of_files) || die "Could not open $list_of_files $!";
open (my $ofh, '>', $second_script_name ) || die "Could not open $second_script_name $!";
print $ofh "cd /home/412992496/fromvan/\n";

# my @files = map { "rm " . $_ } <$ifh>;

print $ofh  map { "rm " . $_ } <$ifh>;

# print $ofh @files;

print $ofh "bye\n";

close $ofh;
close $ifh;

system ("chmod", "744", "$first_script_name");

sub create_first_script {
	open (my $ofh, '>', $first_script_name) || die "Could not open $first_script_name $!";
	print $ofh "#!/usr/bin/ksh\n\nLOGINID=\"412992496\"\n";
	print $ofh "sftp -b ./$second_script_name \$LOGINID\@sftp.gateway.inovisworks.net\n";
	close $ofh;
}
