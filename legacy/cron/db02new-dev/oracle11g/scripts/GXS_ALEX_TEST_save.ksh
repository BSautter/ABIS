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
echo "edi_count: " $edi_count

cd $SEND
# pwd

if [[ $edi_count -gt 0 ]]; then

    rm $SFTP_SEND*.*

    for file in `ls *.edi`
    do
#       chmod 777 $file
       ls -last $file
#       echo "file: " $file "SFTP_SEND: $SFTP_SEND"
       cp $file $SFTP_SEND

       cd $WORK
       sftp -b ./GXS_VAN_ALEX_TEST.ksh $LOGINID@sftp.gateway.inovisworks.net
       ret_code=$?  # Capture sftp return code.
       echo "ret_code: " $ret_code

       cd $SFTP_SEND
#       echo "Before file_2rm in.************************"
#       pwd
       for file_2rm in `ls *.edi`
       do
#           echo "file_2rm: $file_2rm"
#           pwd
#           echo "Before rm file_2rm. File to remove: $SFTP_SEND$file_2rm"
           rm $file_2rm
       done
# exit 
       cd $SEND
#       echo "Before if [[ ret_code -eq  0 ]]"
       pwd

       if [[ $ret_code -eq  0 ]]; then
#          echo "Before mv SENDfile SENDToVan_Bkup. $SEND$file  $SEND$TOVAN_BKUP"
#          ls -last $SEND$file
#          echo "Before mv. File: " $file
#          dos2unix
#          mv $SEND$file $SEND$TOVAN_BKUP
#          echo "After mv SENDfile SENDToVan_Bkup. $SEND$file  $SEND$TOVAN_BKUP"
          mv $file ./ToVan_Bkup/
       fi

#       $file=""
#       $file_2rm=""
    done
fi

exit $ret_code  # Return code of the script is the same as the return code of the sftp.
