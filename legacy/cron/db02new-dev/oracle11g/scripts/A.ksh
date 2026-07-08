#!/usr/bin/ksh

date '+START: %m/%d/%y %H:%M:%S'
RECEIVE="/export/home/oracle11g/edi/receive/"
SEND="/templar/templar/incoming/senddata/"
SFTP_SEND="/templar/templar/incoming/senddata/sftp_send/"
TEMP_FILE="./received_edi_files.txt"
LOGINID="412992496"
UTIL="/templar/templar/util/"
WORK="/export/home/oracle11g/scripts/"
TOVAN_BKUP="ToVan_Bkup"
typeset -i ret_code   # Set return code to an integer.
typeset -i edi_count  # Set return code to an integer.

ret_code=0 # Initialize return code.

edi_count=`ls -l /templar/templar/incoming/senddata/S*.edi 2>/dev/null | wc -l`
# echo "edi_count: " $edi_count

if [[ $edi_count -gt 0 ]]; then

    rm $SFTP_SEND*.* # Just in case... This directory should be empty after each run

    cd $SEND # Directory EDI files are supposed to be sent from

    for file in `ls *.edi`
    do
       cp $file $SFTP_SEND # Copy file to sFTP

       cd $WORK # GXS_VAN.ksh is in this directory

       sftp -b ./GXS_VAN_ALEX_TEST.ksh $LOGINID@sftp.gateway.inovisworks.net # sFTP the file to VAN
       ret_code=$?  # Capture sftp return code.

       cd $SFTP_SEND

       # Delete the file from sftp_send directory regardless of sftp return code
       for file_2rm in `ls *.edi`
       do
           rm $file_2rm
       done

       cd $SEND

       if [[ $ret_code -eq  0 ]]; then
          mv $file ./ToVan_Bkup/  # Move file to ToVan_Bkup directory
       fi
    done
else
    echo "Nothing to upload to VAN"
fi

printf "Return code from sftp: [%d}\n" $ret_code # Record return code to log file.
date '+END: %m/%d/%y %H:%M:%S'
exit $ret_code  # Return code of the script is the same as the return code of the sftp.
