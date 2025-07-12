#!/bin/bash



VERSION=$(grep -Po '(?<=Version = ")[0-9\.]+' Source/Common/Version.cs)

git submodule update --init --recursive || { echo 'git submodule update FAILED' ; exit 1; }

cd Source || exit
dotnet build --configuration Release || { echo 'dotnet build FAILED' ; exit 1; }
cd ..

rm -rf Multiplayer/
mkdir -p Multiplayer
cd Multiplayer || exit

# About/ and Textures/ are shared between all versions
cp -r ../About ../Textures .

cat <<EOF > LoadFolders.xml
<loadFolders>
  <v1.6>
    <li>/</li>
    <li>1.6</li>
  </v1.6>
 
</loadFolders>
EOF


sed -i "/<supportedVersions>/ a \ \ \ \ <li>1.6</li>" About/About.xml
sed -i "/Multiplayer mod for RimWorld./aThis is version ${VERSION}." About/About.xml
sed -i "s/<version>.*<\/version>\$/<version>${VERSION}<\/version>/" About/Manifest.xml

# The current version
mkdir -p 1.6
cp -r ../Assemblies ../AssembliesCustom ../Defs ../Languages 1.6/
rm -f 1.6/Languages/.git 1.6/Languages/LICENSE 1.6/Languages/README.md
 
cd ..

# Zip for Github releases
rm -f Multiplayer-v$VERSION.zip
zip -r -q Multiplayer-v$VERSION.zip Multiplayer

echo "Ok, $PWD/Multiplayer-v$VERSION.zip ready for uploading to Workshop"
