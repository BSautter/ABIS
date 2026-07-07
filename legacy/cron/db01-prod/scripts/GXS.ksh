#!/usr/bin/ksh

date '+START: %m/%d/%y %H:%M:%S'
RECEIVE="/export/home/oracle11g/edi/receive/"
SEND="/templar/templar/incoming/senddata/"
TEMP_FILE="./received_edi_files.txt"
LOGINID="412992496"
UTIL="/templar/templar/util/"
WORK="/export/home/oracle11g/scripts/"
typeset -i ret_code   # Set return code to an integer.
typeset -i edi_count  # Set return code to an integer.

ret_code=0 # Initialize return code.

# Go to working scripts directory.
cd $WORK

sftp -b ./GXS_VAN.ksh  $LOGINID@sftp.gateway.inovisworks.net 

ret_code=$?  # Capture sftp return code.

if [[ $ret_code -ne  0 ]]; then
	echo "ERROR: SFTP failed!"
else

# Go to directory where EDI files were sent from.
	cd $SEND
	edi_count=`ls -l S*.edi 2>/dev/null | wc -l`
	if [[ $edi_count -gt 0 ]]; then
		mv S*.edi ./ToVan_Bkup/  # If sftp was successful then move the files to a backup directory
	else
		echo "No outgoing EDI's sent."
	fi

# Go to directory where EDI files were downloaded to.
	cd $RECEIVE
	
	# Process all of the incoming EDI files.
	# After processing the electronic documents move them to the backup directory.
	edi_count=`ls -l *.edi 2>/dev/null | wc -l`
	if [[ $edi_count -gt 0 ]]; then
		[[ -e $TEMP_FILE ]] && rm $TEMP_FILE # Deletes previous list of file names.
		for file in `ls *.edi`
		do
			$UTIL/postpro $file
			mv $file ./VanDocuments
			echo "$file" >> $TEMP_FILE
		done
		$HOME/scripts/GXS_delete.pl $TEMP_FILE
		$RECEIVE/GXS2.ksh
	else
		echo "No incoming EDI's received."
	fi

fi

printf "Return code from sftp: [%d}\n" $ret_code # Record return code to log file.
date '+STOP: %m/%d/%y %H:%M:%S'
exit $ret_code  # Return code of the script is the same as the return code of the sftp.
