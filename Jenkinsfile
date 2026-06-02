pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '10'))
    }

    environment {
        MASTER_HOST = '192.168.1.10'
        REMOTE_DIR = '/root/adminnet-ci/adminnet'
        SSH_CREDENTIALS_ID = 'master-root-ssh'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Sync Source To Master') {
            steps {
                sshagent(credentials: [env.SSH_CREDENTIALS_ID]) {
                    sh '''
                        set -eux
                        ARCHIVE="/tmp/adminnet-${BUILD_NUMBER}.tgz"
                        tar \
                          --exclude='.git' \
                          --exclude='Web/node_modules' \
                          --exclude='Web/dist' \
                          --exclude='publish' \
                          --exclude='*/bin' \
                          --exclude='*/obj' \
                          -czf "$ARCHIVE" .

                        ssh -o StrictHostKeyChecking=no root@${MASTER_HOST} "rm -rf ${REMOTE_DIR} && mkdir -p ${REMOTE_DIR}"
                        scp -o StrictHostKeyChecking=no "$ARCHIVE" root@${MASTER_HOST}:/tmp/adminnet.tgz
                        ssh -o StrictHostKeyChecking=no root@${MASTER_HOST} "tar xzf /tmp/adminnet.tgz -C ${REMOTE_DIR}"
                    '''
                }
            }
        }

        stage('Build And Deploy') {
            steps {
                sshagent(credentials: [env.SSH_CREDENTIALS_ID]) {
                    sh '''
                        set -eux
                        ssh -o StrictHostKeyChecking=no root@${MASTER_HOST} "cd ${REMOTE_DIR} && bash k8s/jenkins/deploy-adminnet.sh ${BUILD_NUMBER}"
                    '''
                }
            }
        }
    }

    post {
        always {
            sshagent(credentials: [env.SSH_CREDENTIALS_ID]) {
                sh '''
                    ssh -o StrictHostKeyChecking=no root@${MASTER_HOST} "kubectl get pods -n admin-net -o wide || true"
                '''
            }
        }
    }
}
