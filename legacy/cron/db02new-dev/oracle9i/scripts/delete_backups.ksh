#!/usr/bin/ksh -x
#
# Patrick E. P. Reynolds (01/06/2016)
# This script is used in the crontab jobs for both oracle11g and oracle9i.
# Any changes to this script will affect both jobs.
# This script deletes backup files older than 28 days for the respective ID in their backup flie directory.
#	Cool, eh?
#

if [[ "$LOGNAME" = "oracle9i" ]]; then
	cd /u02/exp/spc01
elif [[ "$LOGNAME" = "oracle11g" ]]; then
	cd /u02/exp/abc11
else
	echo "$LOGNAME is unknown.  Exiting script."
	exit
fi

echo "I am in" $PWD "directory."
find . -mtime +28 -exec rm {} \;
